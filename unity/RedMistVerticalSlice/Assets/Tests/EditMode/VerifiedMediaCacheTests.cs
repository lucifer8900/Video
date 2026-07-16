using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class VerifiedMediaCacheTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "redmist-cx306-cache-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ValidDownloadIsHashedAtomicallyAndThenReusedWithoutNetwork()
        {
            byte[] bytes = Bytes(8192, 17);
            var downloader = new RecordingDownloader(bytes);
            var cache = Cache(downloader);
            GenerationDownloadTicket ticket = Ticket(bytes);

            VerifiedMediaHandle first = cache.GetOrDownloadAsync(ticket, CancellationToken.None)
                .GetAwaiter().GetResult();
            VerifiedMediaHandle reused = cache.GetOrDownloadAsync(ticket, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, downloader.DownloadCount);
            Assert.AreEqual(first.LocalPath, reused.LocalPath);
            Assert.AreEqual(ticket.ContentHash, first.ContentHash);
            Assert.AreEqual(bytes.LongLength, first.Length);
            Assert.IsTrue(File.Exists(first.LocalPath));
            Assert.AreEqual(ticket.ContentHash.Substring(7) + ".mp4", Path.GetFileName(first.LocalPath));
            Assert.AreEqual(_root, Path.GetDirectoryName(first.LocalPath));
            Assert.IsEmpty(Directory.GetFiles(_root, "*.part"));
        }

        [Test]
        public void HashMismatchDeletesPartialAndNeverPublishesAPlayableFile()
        {
            byte[] expected = Bytes(4096, 23);
            byte[] received = Bytes(4096, 24);
            var downloader = new RecordingDownloader(received);
            var cache = Cache(downloader);
            GenerationDownloadTicket ticket = Ticket(expected);

            VerifiedMediaCacheException error = CaptureAsync<VerifiedMediaCacheException>(
                    () => cache.GetOrDownloadAsync(ticket, CancellationToken.None))
                .GetAwaiter().GetResult();

            Assert.AreEqual("media.hash_mismatch", error.Code);
            Assert.IsEmpty(Directory.GetFiles(_root));
        }

        [Test]
        public void TruncatedDownloadDeletesPartialAndNeverPublishesAPlayableFile()
        {
            byte[] expected = Bytes(4096, 31);
            byte[] received = expected.Take(1024).ToArray();
            var downloader = new RecordingDownloader(received);
            var cache = Cache(downloader);
            GenerationDownloadTicket ticket = Ticket(expected);

            VerifiedMediaCacheException error = CaptureAsync<VerifiedMediaCacheException>(
                    () => cache.GetOrDownloadAsync(ticket, CancellationToken.None))
                .GetAwaiter().GetResult();

            Assert.AreEqual("media.length_mismatch", error.Code);
            Assert.IsEmpty(Directory.GetFiles(_root));
        }

        [TestCase("http://media.example.test/video.mp4", "media.ticket_url_invalid")]
        [TestCase("https://other.example.test/video.mp4", "media.ticket_host_rejected")]
        [TestCase("https://user:secret@media.example.test/video.mp4", "media.ticket_url_invalid")]
        [TestCase("https://media.example.test/video.mp4#fragment", "media.ticket_url_invalid")]
        public void UnsafeDownloadUrlIsRejectedBeforeNetwork(string url, string expectedCode)
        {
            byte[] bytes = Bytes(128, 41);
            var downloader = new RecordingDownloader(bytes);
            var cache = Cache(downloader);
            GenerationDownloadTicket source = Ticket(bytes);
            var ticket = new GenerationDownloadTicket(
                source.MediaId,
                source.ContentHash,
                url,
                source.ExpiresAtUtc,
                source.ContentType,
                source.Length);

            VerifiedMediaCacheException error = CaptureAsync<VerifiedMediaCacheException>(
                    () => cache.GetOrDownloadAsync(ticket, CancellationToken.None))
                .GetAwaiter().GetResult();

            Assert.AreEqual(expectedCode, error.Code);
            Assert.AreEqual(0, downloader.DownloadCount);
            Assert.IsEmpty(Directory.GetFiles(_root));
        }

        [Test]
        public void ExpiredOrOversizedTicketIsRejectedBeforeNetwork()
        {
            byte[] oversizedBytes = Bytes(128, 51);
            byte[] expiredBytes = Bytes(32, 52);
            var downloader = new RecordingDownloader(oversizedBytes);
            var cache = Cache(downloader, maxBytes: 64);
            GenerationDownloadTicket source = Ticket(oversizedBytes);
            GenerationDownloadTicket expiringSource = Ticket(expiredBytes);
            var expired = new GenerationDownloadTicket(
                expiringSource.MediaId,
                expiringSource.ContentHash,
                expiringSource.DownloadUrl,
                FixedUtcNow.AddSeconds(-1),
                expiringSource.ContentType,
                expiringSource.Length);

            Assert.AreEqual(
                "media.ticket_expired",
                CaptureAsync<VerifiedMediaCacheException>(
                        () => cache.GetOrDownloadAsync(expired, CancellationToken.None))
                    .GetAwaiter().GetResult().Code);
            Assert.AreEqual(
                "media.length_invalid",
                CaptureAsync<VerifiedMediaCacheException>(
                        () => cache.GetOrDownloadAsync(source, CancellationToken.None))
                    .GetAwaiter().GetResult().Code);
            Assert.AreEqual(0, downloader.DownloadCount);
        }

        [Test]
        public void DownloadFailureRemovesPartialFileAndPreservesTransportClassification()
        {
            byte[] bytes = Bytes(512, 61);
            var downloader = new RecordingDownloader(bytes)
            {
                Failure = new GenerationTransportException(
                    GenerationTransportFailure.NetworkUnavailable)
            };
            var cache = Cache(downloader);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => cache.GetOrDownloadAsync(Ticket(bytes), CancellationToken.None))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationTransportFailure.NetworkUnavailable, error.Failure);
            Assert.IsEmpty(Directory.GetFiles(_root));
        }

        private static readonly DateTimeOffset FixedUtcNow =
            new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

        private VerifiedMediaCache Cache(IVerifiedMediaDownloader downloader, long maxBytes = 1024 * 1024)
        {
            return new VerifiedMediaCache(
                _root,
                new[] { "media.example.test" },
                maxBytes,
                downloader,
                () => FixedUtcNow);
        }

        private static GenerationDownloadTicket Ticket(byte[] bytes)
        {
            return new GenerationDownloadTicket(
                "media.fixture",
                Hash(bytes),
                "https://media.example.test/signed/video.mp4?fixture=not-a-secret",
                FixedUtcNow.AddMinutes(5),
                "video/mp4",
                bytes.LongLength);
        }

        private static byte[] Bytes(int length, int seed)
        {
            var bytes = new byte[length];
            var random = new Random(seed);
            random.NextBytes(bytes);
            return bytes;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return "sha256:" + string.Concat(
                    algorithm.ComputeHash(bytes).Select(value => value.ToString("x2")));
            }
        }

        private static async Task<TException> CaptureAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }
            Assert.Fail("Expected exception " + typeof(TException).Name + ".");
            return null;
        }

        private sealed class RecordingDownloader : IVerifiedMediaDownloader
        {
            private readonly byte[] _bytes;

            public RecordingDownloader(byte[] bytes)
            {
                _bytes = bytes;
            }

            public Exception Failure { get; set; }
            public int DownloadCount { get; private set; }

            public Task DownloadAsync(
                GenerationDownloadTicket ticket,
                string destinationPath,
                CancellationToken cancellationToken)
            {
                DownloadCount++;
                cancellationToken.ThrowIfCancellationRequested();
                File.WriteAllBytes(destinationPath, _bytes);
                if (Failure != null) throw Failure;
                return Task.FromResult(0);
            }
        }
    }
}
