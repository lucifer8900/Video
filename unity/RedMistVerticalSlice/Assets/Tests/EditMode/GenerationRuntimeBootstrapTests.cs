using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class GenerationRuntimeBootstrapTests
    {
        private const string Hash =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string JobId = "1f6ea513-5a59-4f17-87bd-8a637dccd321";

        private string _root;
        private string _streamingAssets;
        private string _cacheRoot;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "redmist-cx306-bootstrap-tests",
                Guid.NewGuid().ToString("N"));
            _streamingAssets = Path.Combine(_root, "StreamingAssets");
            _cacheRoot = Path.Combine(_root, "Cache");
            Directory.CreateDirectory(Path.Combine(_streamingAssets, "Config"));
            Directory.CreateDirectory(_cacheRoot);
            WriteConfiguration(DisabledJson());
            _gameObject = new GameObject("GenerationRuntimeBootstrapTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void CheckedInStyleDisabledConfigurationCreatesNoTransportAndStillPlaysFallback()
        {
            int transportFactoryCalls = 0;
            var media = new RecordingMediaPort();
            GenerationRuntimeBootstrap bootstrap = CreateBootstrap();

            bootstrap.Initialize(
                media,
                _streamingAssets,
                _cacheRoot,
                () =>
                {
                    transportFactoryCalls++;
                    return new RecordingHttpTransport();
                });
            GenerationPlaybackRequest request = bootstrap.CreateRequest(
                "node.attempt-1",
                Hash,
                "prologue");
            GenerationPlaybackResult result = bootstrap.RunAsync(
                    "node.attempt-1",
                    Hash,
                    "prologue")
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(bootstrap.IsInitialized);
            Assert.IsTrue(bootstrap.ConfigurationValid);
            Assert.IsFalse(bootstrap.OnlineEnabled);
            Assert.IsFalse(bootstrap.TransportInstalled);
            Assert.IsFalse(request.OnlineGenerationEnabled);
            Assert.AreEqual(0, transportFactoryCalls);
            Assert.AreEqual(1, media.FallbackPlayCount);
            Assert.AreEqual("prologue", media.LastFallbackCueId);
            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(
                GenerationPlaybackFailure.OnlineGenerationDisabled,
                result.Failure);
        }

        [Test]
        public void EnabledConfigurationAloneInstallsTransportAndControlsRequestFlag()
        {
            WriteConfiguration(EnabledJson());
            int transportFactoryCalls = 0;
            var transport = new RecordingHttpTransport();
            GenerationRuntimeBootstrap bootstrap = CreateBootstrap();

            bootstrap.Initialize(
                new RecordingMediaPort(),
                _streamingAssets,
                _cacheRoot,
                () =>
                {
                    transportFactoryCalls++;
                    return transport;
                });
            GenerationPlaybackRequest request = bootstrap.CreateRequest(
                "node.attempt-2",
                Hash,
                "prologue");

            Assert.IsTrue(bootstrap.ConfigurationValid);
            Assert.IsTrue(bootstrap.OnlineEnabled);
            Assert.IsTrue(bootstrap.TransportInstalled);
            Assert.IsTrue(request.OnlineGenerationEnabled);
            Assert.AreEqual(1, transportFactoryCalls);
            Assert.AreEqual(0, transport.SendCount);
            Assert.AreEqual(0, transport.DownloadCount);
        }

        [Test]
        public void InvalidConfigurationFailsClosedWithoutCreatingTransport()
        {
            WriteConfiguration(EnabledJson().Replace(
                "\"maxDownloadBytes\":536870912",
                "\"maxDownloadBytes\":536870912,\"apiKey\":\"forbidden\""));
            int transportFactoryCalls = 0;
            var media = new RecordingMediaPort();
            GenerationRuntimeBootstrap bootstrap = CreateBootstrap();

            bootstrap.Initialize(
                media,
                _streamingAssets,
                _cacheRoot,
                () =>
                {
                    transportFactoryCalls++;
                    return new RecordingHttpTransport();
                });
            GenerationPlaybackRequest request = bootstrap.CreateRequest(
                "node.attempt-3",
                Hash,
                "prologue");
            GenerationPlaybackResult result = bootstrap.RunAsync(
                    "node.attempt-3",
                    Hash,
                    "prologue")
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(bootstrap.IsInitialized);
            Assert.IsFalse(bootstrap.ConfigurationValid);
            Assert.IsFalse(bootstrap.OnlineEnabled);
            Assert.IsFalse(bootstrap.TransportInstalled);
            Assert.IsFalse(request.OnlineGenerationEnabled);
            Assert.AreEqual(0, transportFactoryCalls);
            Assert.AreEqual(1, media.FallbackPlayCount);
            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(
                GenerationPlaybackFailure.OnlineGenerationDisabled,
                result.Failure);
        }

        [Test]
        public void PublicSurfaceCannotAcceptCallerForgedGenerationPlaybackRequest()
        {
            bool acceptsForgedRequest = typeof(GenerationRuntimeBootstrap)
                .GetMethods()
                .Where(method => method.IsPublic)
                .SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(GenerationPlaybackRequest));

            Assert.IsFalse(acceptsForgedRequest);
            Assert.IsNull(typeof(GenerationRuntimeBootstrap).GetProperty("Coordinator"));
        }

        [Test]
        public void UnityClockUsesMonotonicTimeAndHonorsCancellation()
        {
            var clock = new UnityGenerationFlowClock();
            double before = clock.MonotonicSeconds;
            double after = clock.MonotonicSeconds;
            DateTimeOffset utcNow = clock.UtcNow;
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.GreaterOrEqual(after, before);
            Assert.Less(
                Math.Abs((DateTimeOffset.UtcNow - utcNow).TotalSeconds),
                5d);
            Assert.Catch<OperationCanceledException>(() =>
                clock.DelayAsync(TimeSpan.FromSeconds(1), cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
        }

        [Test]
        public void ModerationResponseFlowsFromRealParserToExactlyOneFallbackWithoutDownload()
        {
            GenerationRuntimeOptions options = GenerationRuntimeOptionsLoader.Parse(EnabledJson());
            var transport = new RecordingHttpTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(
                202,
                "{" +
                "\"schemaVersion\":\"1.0.0\"," +
                "\"requestId\":\"req.1\"," +
                "\"jobId\":\"" + JobId + "\"," +
                "\"status\":\"failed\"," +
                "\"terminal\":true," +
                "\"pollAfterMilliseconds\":0," +
                "\"failureCode\":\"moderation.rejected\"" +
                "}"));
            var client = new GenerationApiClient(options, transport);
            var cache = new RecordingCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = new GenerationPlaybackCoordinator(
                client,
                cache,
                playback,
                new ImmediateClock());
            var request = new GenerationPlaybackRequest(
                "node.moderation-1",
                Hash,
                "prologue",
                true);

            GenerationPlaybackResult result = coordinator.RunAsync(request)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationPlaybackFailure.ModerationRejected, result.Failure);
            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(1, transport.SendCount);
            Assert.AreEqual(0, transport.DownloadCount);
            Assert.AreEqual(0, cache.CallCount);
            Assert.AreEqual(1, playback.FallbackCount);
            Assert.AreEqual(0, playback.GeneratedCount);
        }

        private GenerationRuntimeBootstrap CreateBootstrap()
        {
            return _gameObject.AddComponent<GenerationRuntimeBootstrap>();
        }

        private void WriteConfiguration(string json)
        {
            File.WriteAllText(
                Path.Combine(_streamingAssets, "Config", "generation.runtime.json"),
                json);
        }

        private static string DisabledJson()
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"enabled\":false," +
                   "\"apiBaseUrl\":\"\"," +
                   "\"allowedMediaHosts\":[]," +
                   "\"requestTimeoutSeconds\":10," +
                   "\"maxResponseBytes\":262144," +
                   "\"maxDownloadBytes\":536870912" +
                   "}";
        }

        private static string EnabledJson()
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"enabled\":true," +
                   "\"apiBaseUrl\":\"https://api.example.test/\"," +
                   "\"allowedMediaHosts\":[\"media.example.test\"]," +
                   "\"requestTimeoutSeconds\":10," +
                   "\"maxResponseBytes\":262144," +
                   "\"maxDownloadBytes\":536870912" +
                   "}";
        }

        private sealed class RecordingMediaPort : IMediaDirectorPlaybackPort
        {
            public int FallbackPlayCount { get; private set; }
            public string LastFallbackCueId { get; private set; }

            public bool PlayVerified(
                VerifiedMediaHandle media,
                string cacheRoot,
                Action<MediaEndReason> finished)
            {
                finished?.Invoke(MediaEndReason.Completed);
                return true;
            }

            public bool Play(string cueId, Action<MediaEndReason> finished)
            {
                FallbackPlayCount++;
                LastFallbackCueId = cueId;
                finished?.Invoke(MediaEndReason.Completed);
                return true;
            }

            public void Skip()
            {
            }
        }

        private sealed class RecordingHttpTransport : IGenerationHttpTransport
        {
            public Queue<GenerationHttpResponse> Responses { get; } =
                new Queue<GenerationHttpResponse>();
            public int SendCount { get; private set; }
            public int DownloadCount { get; private set; }

            public Task<GenerationHttpResponse> SendAsync(
                GenerationHttpRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SendCount++;
                if (Responses.Count == 0)
                    throw new InvalidOperationException("No fake response was configured.");
                return Task.FromResult(Responses.Dequeue());
            }

            public Task DownloadAsync(
                GenerationDownloadRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingCache : IVerifiedMediaCache
        {
            public int CallCount { get; private set; }

            public Task<VerifiedMediaHandle> GetOrDownloadAsync(
                GenerationDownloadTicket ticket,
                CancellationToken cancellationToken)
            {
                CallCount++;
                throw new InvalidOperationException("Moderation failure must not use cache.");
            }
        }

        private sealed class RecordingPlaybackGateway : IMediaPlaybackGateway
        {
            public int GeneratedCount { get; private set; }
            public int FallbackCount { get; private set; }

            public Task<MediaPlaybackCompletion> PlayGeneratedAsync(
                VerifiedMediaHandle media,
                CancellationToken cancellationToken)
            {
                GeneratedCount++;
                return Task.FromResult(MediaPlaybackCompletion.Completed);
            }

            public Task<MediaPlaybackCompletion> PlayFallbackAsync(
                string fallbackCueId,
                CancellationToken cancellationToken)
            {
                FallbackCount++;
                return Task.FromResult(MediaPlaybackCompletion.Completed);
            }
        }

        private sealed class ImmediateClock : IGenerationFlowClock
        {
            public double MonotonicSeconds => 0d;
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
