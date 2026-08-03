using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lingmai.RedMist.Tests
{
    public sealed class MediaDirectorPlaybackGatewayTests
    {
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "redmist-cx306-playback-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_cacheRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
        }

        [Test]
        public void VerifiedMediaPolicyAcceptsOnlyExactHashNamedFileWithMatchingContentAndLength()
        {
            byte[] bytes = Bytes(2048, 17);
            string hash = Hash(bytes);
            string path = Path.Combine(_cacheRoot, hash.Substring(7) + ".mp4");
            File.WriteAllBytes(path, bytes);
            var media = new VerifiedMediaHandle(hash, path, bytes.LongLength);

            Assert.IsTrue(MediaDirector.IsPlayableVerifiedMedia(media, _cacheRoot));

            byte[] replaced = Bytes(bytes.Length, 18);
            File.WriteAllBytes(path, replaced);
            Assert.IsFalse(
                MediaDirector.IsPlayableVerifiedMedia(media, _cacheRoot),
                "A same-length file modified after cache verification must not become playable.");
        }

        [Test]
        public void VerifiedMediaPolicyRejectsOutsideNestedWrongNameWrongLengthAndUrlPaths()
        {
            byte[] bytes = Bytes(256, 23);
            string hash = Hash(bytes);
            string expectedName = hash.Substring(7) + ".mp4";
            string outsideRoot = Path.Combine(
                Path.GetTempPath(),
                "redmist-cx306-playback-outside-" + Guid.NewGuid().ToString("N"));
            string nestedRoot = Path.Combine(_cacheRoot, "nested");
            Directory.CreateDirectory(outsideRoot);
            Directory.CreateDirectory(nestedRoot);

            try
            {
                string outside = Path.Combine(outsideRoot, expectedName);
                string nested = Path.Combine(nestedRoot, expectedName);
                string wrongName = Path.Combine(_cacheRoot, "generated.mp4");
                string correct = Path.Combine(_cacheRoot, expectedName);
                File.WriteAllBytes(outside, bytes);
                File.WriteAllBytes(nested, bytes);
                File.WriteAllBytes(wrongName, bytes);
                File.WriteAllBytes(correct, bytes);

                Assert.IsFalse(MediaDirector.IsPlayableVerifiedMedia(
                    new VerifiedMediaHandle(hash, outside, bytes.LongLength),
                    _cacheRoot));
                Assert.IsFalse(MediaDirector.IsPlayableVerifiedMedia(
                    new VerifiedMediaHandle(hash, nested, bytes.LongLength),
                    _cacheRoot));
                Assert.IsFalse(MediaDirector.IsPlayableVerifiedMedia(
                    new VerifiedMediaHandle(hash, wrongName, bytes.LongLength),
                    _cacheRoot));
                Assert.IsFalse(MediaDirector.IsPlayableVerifiedMedia(
                    new VerifiedMediaHandle(hash, correct, bytes.LongLength + 1),
                    _cacheRoot));
                Assert.IsFalse(MediaDirector.IsPlayableVerifiedMedia(
                    new VerifiedMediaHandle(hash, "https://media.example.test/signed.mp4", bytes.LongLength),
                    _cacheRoot));
            }
            finally
            {
                if (Directory.Exists(outsideRoot)) Directory.Delete(outsideRoot, true);
            }
        }

        [Test]
        public void DirectorRejectsUnsafeGeneratedPathWithoutLoggingIt()
        {
            byte[] bytes = Bytes(64, 29);
            string hash = Hash(bytes);
            string secretMarker = "signed-url-must-not-leak-" + Guid.NewGuid().ToString("N");
            string outsidePath = Path.Combine(Path.GetTempPath(), secretMarker + ".mp4");
            File.WriteAllBytes(outsidePath, bytes);
            var media = new VerifiedMediaHandle(hash, outsidePath, bytes.LongLength);
            var gameObject = new GameObject("cx306-media-director-test");

            try
            {
                var director = gameObject.AddComponent<MediaDirector>();
                MediaEndReason? completion = null;
                LogAssert.Expect(LogType.Warning, "RED_MIST_GENERATED_MEDIA_REJECTED");

                bool started = director.PlayVerified(
                    media,
                    _cacheRoot,
                    reason => completion = reason);

                Assert.IsFalse(started);
                Assert.AreEqual(MediaEndReason.Error, completion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (File.Exists(outsidePath)) File.Delete(outsidePath);
            }
        }

        [TestCase(MediaEndReason.Completed, MediaPlaybackCompletion.Completed)]
        [TestCase(MediaEndReason.Skipped, MediaPlaybackCompletion.Skipped)]
        [TestCase(MediaEndReason.Timeout, MediaPlaybackCompletion.TimedOut)]
        [TestCase(MediaEndReason.Busy, MediaPlaybackCompletion.Busy)]
        [TestCase(MediaEndReason.Missing, MediaPlaybackCompletion.Failed)]
        [TestCase(MediaEndReason.Error, MediaPlaybackCompletion.Failed)]
        public void GeneratedPlaybackMapsDirectorCompletion(
            MediaEndReason directorCompletion,
            MediaPlaybackCompletion expected)
        {
            byte[] bytes = Bytes(64, 31);
            VerifiedMediaHandle media = WriteVerified(bytes);
            var port = new FakeMediaDirectorPlaybackPort
            {
                GeneratedCompletion = directorCompletion
            };
            var gateway = new MediaDirectorPlaybackGateway(port, _cacheRoot);

            MediaPlaybackCompletion actual = gateway.PlayGeneratedAsync(
                    media,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(expected, actual);
            Assert.AreSame(media, port.GeneratedMedia);
            Assert.AreEqual(Path.GetFullPath(_cacheRoot), port.GeneratedCacheRoot);
            Assert.AreEqual(1, port.GeneratedPlayCount);
            Assert.AreEqual(0, port.FallbackPlayCount);
        }

        [Test]
        public void RejectedRequestWithoutCallbackCompletesAsFailureInsteadOfHanging()
        {
            VerifiedMediaHandle media = WriteVerified(Bytes(64, 37));
            var port = new FakeMediaDirectorPlaybackPort { AcceptGenerated = false };
            var gateway = new MediaDirectorPlaybackGateway(port, _cacheRoot);

            MediaPlaybackCompletion result = gateway.PlayGeneratedAsync(
                    media,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(MediaPlaybackCompletion.Failed, result);
        }

        [Test]
        public void FallbackUsesApprovedCuePathAndMapsCompletion()
        {
            var port = new FakeMediaDirectorPlaybackPort
            {
                FallbackCompletion = MediaEndReason.Completed
            };
            var gateway = new MediaDirectorPlaybackGateway(port, _cacheRoot);

            MediaPlaybackCompletion result = gateway.PlayFallbackAsync(
                    "approved.fixture.cue",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(MediaPlaybackCompletion.Completed, result);
            Assert.AreEqual("approved.fixture.cue", port.FallbackCueId);
            Assert.AreEqual(1, port.FallbackPlayCount);
            Assert.AreEqual(0, port.GeneratedPlayCount);
        }

        [Test]
        public void PreCanceledPlaybackDoesNotReachMediaDirector()
        {
            VerifiedMediaHandle media = WriteVerified(Bytes(64, 41));
            var port = new FakeMediaDirectorPlaybackPort();
            var gateway = new MediaDirectorPlaybackGateway(port, _cacheRoot);
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Task<MediaPlaybackCompletion> task = gateway.PlayGeneratedAsync(
                media,
                cancellation.Token);

            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, port.GeneratedPlayCount);
            Assert.AreEqual(0, port.SkipCount);
        }

        [Test]
        public void CancellationStopsOnlyThePlaybackAcceptedByThisRequest()
        {
            VerifiedMediaHandle media = WriteVerified(Bytes(64, 43));
            var port = new FakeMediaDirectorPlaybackPort { CompleteSynchronously = false };
            var gateway = new MediaDirectorPlaybackGateway(port, _cacheRoot);
            var cancellation = new CancellationTokenSource();

            Task<MediaPlaybackCompletion> task = gateway.PlayGeneratedAsync(
                media,
                cancellation.Token);
            cancellation.Cancel();

            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(1, port.GeneratedPlayCount);
            Assert.AreEqual(1, port.SkipCount);
        }

        private VerifiedMediaHandle WriteVerified(byte[] bytes)
        {
            string hash = Hash(bytes);
            string path = Path.Combine(_cacheRoot, hash.Substring(7) + ".mp4");
            File.WriteAllBytes(path, bytes);
            return new VerifiedMediaHandle(hash, path, bytes.LongLength);
        }

        private static byte[] Bytes(int length, int seed)
        {
            var bytes = new byte[length];
            new System.Random(seed).NextBytes(bytes);
            return bytes;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                const string alphabet = "0123456789abcdef";
                var characters = new char[hash.Length * 2];
                for (int index = 0; index < hash.Length; index++)
                {
                    characters[index * 2] = alphabet[hash[index] >> 4];
                    characters[index * 2 + 1] = alphabet[hash[index] & 15];
                }
                return "sha256:" + new string(characters);
            }
        }

        private sealed class FakeMediaDirectorPlaybackPort : IMediaDirectorPlaybackPort
        {
            public bool AcceptGenerated { get; set; } = true;
            public bool CompleteSynchronously { get; set; } = true;
            public MediaEndReason GeneratedCompletion { get; set; } = MediaEndReason.Completed;
            public MediaEndReason FallbackCompletion { get; set; } = MediaEndReason.Completed;
            public VerifiedMediaHandle GeneratedMedia { get; private set; }
            public string GeneratedCacheRoot { get; private set; }
            public string FallbackCueId { get; private set; }
            public int GeneratedPlayCount { get; private set; }
            public int FallbackPlayCount { get; private set; }
            public int SkipCount { get; private set; }

            public bool PlayVerified(
                VerifiedMediaHandle media,
                string cacheRoot,
                Action<MediaEndReason> finished)
            {
                GeneratedPlayCount++;
                GeneratedMedia = media;
                GeneratedCacheRoot = cacheRoot;
                if (AcceptGenerated && CompleteSynchronously) finished(GeneratedCompletion);
                return AcceptGenerated;
            }

            public bool Play(string cueId, Action<MediaEndReason> finished)
            {
                FallbackPlayCount++;
                FallbackCueId = cueId;
                finished(FallbackCompletion);
                return true;
            }

            public void Skip()
            {
                SkipCount++;
            }
        }
    }
}
