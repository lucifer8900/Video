using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class GenerationPlaybackCoordinatorTests
    {
        [Test]
        public void SuccessPollsUntilReadyThenCachesAndPlaysVerifiedMedia()
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-success", GenerationJobState.Queued)
            };
            gateway.PollResults.Enqueue(Job("job-success", GenerationJobState.Generating));
            gateway.PollResults.Enqueue(Job("job-success", GenerationJobState.Moderating));
            gateway.PollResults.Enqueue(Job("job-success", GenerationJobState.Transcoding));
            gateway.PollResults.Enqueue(Job("job-success", GenerationJobState.Ready));
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            GenerationPlaybackResult result = coordinator.RunAsync(Request("success", 'a'))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.None, result.Failure);
            Assert.AreEqual("job-success", result.JobId);
            Assert.AreEqual(1, gateway.CreateCount);
            Assert.AreEqual(4, gateway.PollCount);
            Assert.AreEqual(1, gateway.TicketCount);
            Assert.AreEqual(1, cache.AcquireCount);
            Assert.AreEqual(1, playback.GeneratedPlayCount);
            Assert.AreEqual(0, playback.FallbackPlayCount);
        }

        [Test]
        public void TotalDeadlineStopsEndlessPollingAndPlaysFallbackExactlyOnce()
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-timeout", GenerationJobState.Queued),
                RepeatLastPollResult = Job("job-timeout", GenerationJobState.Queued)
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            GenerationPlaybackResult result = coordinator.RunAsync(
                    Request("timeout", 'b', totalTimeoutSeconds: 3d, pollIntervalSeconds: 1d))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.TotalTimeout, result.Failure);
            Assert.GreaterOrEqual(gateway.PollCount, 2);
            Assert.AreEqual(0, gateway.TicketCount);
            Assert.AreEqual(0, cache.AcquireCount);
            Assert.AreEqual(0, playback.GeneratedPlayCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        [Test]
        public void ModerationFailedJobNeverDownloadsAndPlaysFallbackExactlyOnce()
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-rejected", GenerationJobState.Failed, "moderation.rejected")
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            GenerationPlaybackResult result = coordinator.RunAsync(Request("rejected", 'c'))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.ModerationRejected, result.Failure);
            Assert.AreEqual(0, gateway.PollCount);
            Assert.AreEqual(0, gateway.TicketCount);
            Assert.AreEqual(0, cache.AcquireCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        [Test]
        public void ExpiredDownloadTicketIsRejectedBeforeCacheAndFallsBackExactlyOnce()
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-expired-ticket", GenerationJobState.Ready),
                TicketExpiresAtUtc = AdvancingClock.InitialUtc.AddSeconds(-1d)
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            GenerationPlaybackResult result = coordinator.RunAsync(Request("expired-ticket", '7'))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.DownloadTicketExpired, result.Failure);
            Assert.AreEqual(1, gateway.TicketCount);
            Assert.AreEqual(0, cache.AcquireCount);
            Assert.AreEqual(0, playback.GeneratedPlayCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        [Test]
        public void ConcurrentSameKeyAndHashShareOneFlowAndBothSubscribersComplete()
        {
            var createCompletion = new TaskCompletionSource<GenerationJobSnapshot>();
            var gateway = new ScriptedGenerationGateway
            {
                CreateAsyncOverride = (_, __) => createCompletion.Task
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());
            GenerationPlaybackRequest request = Request("duplicate", 'd');
            int completedSubscribers = 0;

            Task<GenerationPlaybackResult> first = ObserveAsync(
                coordinator.RunAsync(request),
                () => Interlocked.Increment(ref completedSubscribers));
            Task<GenerationPlaybackResult> second = ObserveAsync(
                coordinator.RunAsync(request),
                () => Interlocked.Increment(ref completedSubscribers));
            createCompletion.SetResult(Job("job-duplicate", GenerationJobState.Ready));
            GenerationPlaybackResult[] results = Task.WhenAll(first, second)
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, completedSubscribers);
            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, results[0].Outcome);
            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, results[1].Outcome);
            Assert.AreEqual(results[0].JobId, results[1].JobId);
            Assert.AreEqual(1, gateway.CreateCount);
            Assert.AreEqual(1, gateway.TicketCount);
            Assert.AreEqual(1, cache.AcquireCount);
            Assert.AreEqual(1, playback.GeneratedPlayCount);
            Assert.AreEqual(0, playback.FallbackPlayCount);
        }

        [Test]
        public void SequentialSameKeyAndHashReplaysMediaWithoutRetainingCompletedTask()
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-sequential-replay", GenerationJobState.Ready)
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());
            GenerationPlaybackRequest request = Request("sequential-replay", '8');

            GenerationPlaybackResult first = coordinator.RunAsync(request)
                .GetAwaiter().GetResult();
            GenerationPlaybackResult second = coordinator.RunAsync(request)
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, first.Outcome);
            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, second.Outcome);
            Assert.AreEqual(2, gateway.CreateCount);
            Assert.AreEqual(2, gateway.TicketCount);
            Assert.AreEqual(2, cache.AcquireCount);
            Assert.AreEqual(2, playback.GeneratedPlayCount);
        }

        [Test]
        public void SameKeyWithDifferentHashDoesNotSubmitAgainAndConflictFallsBackOnce()
        {
            var createCompletion = new TaskCompletionSource<GenerationJobSnapshot>();
            var gateway = new ScriptedGenerationGateway
            {
                CreateAsyncOverride = (_, __) => createCompletion.Task
            };
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            Task<GenerationPlaybackResult> firstTask = coordinator.RunAsync(Request("conflict", 'e'));
            GenerationPlaybackResult conflict = coordinator.RunAsync(Request("conflict", 'f'))
                .GetAwaiter().GetResult();
            createCompletion.SetResult(Job("job-first", GenerationJobState.Ready));
            GenerationPlaybackResult first = firstTask.GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.GeneratedMediaPlayed, first.Outcome);
            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, conflict.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.IdempotencyConflict, conflict.Failure);
            Assert.AreEqual(1, gateway.CreateCount);
            Assert.AreEqual(1, gateway.TicketCount);
            Assert.AreEqual(1, cache.AcquireCount);
            Assert.AreEqual(1, playback.GeneratedPlayCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        [TestCase(TransportFailurePoint.Create)]
        [TestCase(TransportFailurePoint.Download)]
        public void NetworkFailureAtCreateOrDownloadPlaysFallbackExactlyOnce(
            TransportFailurePoint failurePoint)
        {
            var gateway = new ScriptedGenerationGateway
            {
                CreateResult = Job("job-network", GenerationJobState.Ready)
            };
            var cache = new RecordingMediaCache();
            if (failurePoint == TransportFailurePoint.Create)
            {
                gateway.CreateAsyncOverride = (_, __) =>
                    Faulted<GenerationJobSnapshot>(new GenerationTransportException(
                        GenerationTransportFailure.NetworkUnavailable));
            }
            else
            {
                cache.GetOrDownloadAsyncOverride = (_, __) =>
                    Faulted<VerifiedMediaHandle>(new GenerationTransportException(
                        GenerationTransportFailure.NetworkUnavailable));
            }
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());

            GenerationPlaybackResult result = coordinator.RunAsync(Request("network-" + failurePoint, '1'))
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.NetworkUnavailable, result.Failure);
            Assert.AreEqual(1, gateway.CreateCount);
            Assert.AreEqual(failurePoint == TransportFailurePoint.Download ? 1 : 0, gateway.TicketCount);
            Assert.AreEqual(failurePoint == TransportFailurePoint.Download ? 1 : 0, cache.AcquireCount);
            Assert.AreEqual(0, playback.GeneratedPlayCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        [Test]
        public void DefaultRequestIsOfflineAndNeverTouchesGenerationGateway()
        {
            var gateway = new ScriptedGenerationGateway();
            var cache = new RecordingMediaCache();
            var playback = new RecordingPlaybackGateway();
            var coordinator = Coordinator(gateway, cache, playback, new AdvancingClock());
            var request = new GenerationPlaybackRequest(
                "default-offline",
                Hash('2'),
                "approved-fallback");

            GenerationPlaybackResult result = coordinator.RunAsync(request)
                .GetAwaiter().GetResult();

            Assert.AreEqual(GenerationPlaybackOutcome.FallbackPlayed, result.Outcome);
            Assert.AreEqual(GenerationPlaybackFailure.OnlineGenerationDisabled, result.Failure);
            Assert.AreEqual(0, gateway.CreateCount);
            Assert.AreEqual(0, gateway.PollCount);
            Assert.AreEqual(0, gateway.TicketCount);
            Assert.AreEqual(0, cache.AcquireCount);
            Assert.AreEqual(1, playback.FallbackPlayCount);
        }

        private static GenerationPlaybackCoordinator Coordinator(
            IGenerationJobGateway gateway,
            IVerifiedMediaCache cache,
            IMediaPlaybackGateway playback,
            IGenerationFlowClock clock)
        {
            return new GenerationPlaybackCoordinator(gateway, cache, playback, clock);
        }

        private static GenerationPlaybackRequest Request(
            string suffix,
            char hashCharacter,
            double totalTimeoutSeconds = 20d,
            double pollIntervalSeconds = 1d)
        {
            return new GenerationPlaybackRequest(
                "idempotency-" + suffix,
                Hash(hashCharacter),
                "approved-fallback",
                true,
                totalTimeoutSeconds,
                pollIntervalSeconds);
        }

        private static string Hash(char character)
        {
            return "sha256:" + new string(character, 64);
        }

        private static GenerationJobSnapshot Job(
            string id,
            GenerationJobState state,
            string failureCode = null)
        {
            return new GenerationJobSnapshot(id, state, failureCode);
        }

        private static async Task<GenerationPlaybackResult> ObserveAsync(
            Task<GenerationPlaybackResult> task,
            Action completed)
        {
            GenerationPlaybackResult result = await task;
            completed();
            return result;
        }

        private static Task<T> Faulted<T>(Exception exception)
        {
            var completion = new TaskCompletionSource<T>();
            completion.SetException(exception);
            return completion.Task;
        }

        public enum TransportFailurePoint
        {
            Create,
            Download
        }

        private sealed class ScriptedGenerationGateway : IGenerationJobGateway
        {
            public GenerationJobSnapshot CreateResult { get; set; }
            public Func<GenerationPlaybackRequest, CancellationToken, Task<GenerationJobSnapshot>>
                CreateAsyncOverride { get; set; }
            public Queue<GenerationJobSnapshot> PollResults { get; } =
                new Queue<GenerationJobSnapshot>();
            public GenerationJobSnapshot RepeatLastPollResult { get; set; }
            public DateTimeOffset TicketExpiresAtUtc { get; set; } =
                AdvancingClock.InitialUtc.AddMinutes(5d);
            public int CreateCount { get; private set; }
            public int PollCount { get; private set; }
            public int TicketCount { get; private set; }

            public Task<GenerationJobSnapshot> CreateOrGetAsync(
                GenerationPlaybackRequest request,
                CancellationToken cancellationToken)
            {
                CreateCount++;
                if (CreateAsyncOverride != null)
                    return CreateAsyncOverride(request, cancellationToken);
                if (CreateResult == null)
                    throw new AssertionException("The generation gateway must not be called in this test.");
                return Task.FromResult(CreateResult);
            }

            public Task<GenerationJobSnapshot> GetAsync(
                string jobId,
                CancellationToken cancellationToken)
            {
                PollCount++;
                if (PollResults.Count > 0) return Task.FromResult(PollResults.Dequeue());
                if (RepeatLastPollResult != null) return Task.FromResult(RepeatLastPollResult);
                throw new AssertionException("No scripted poll response remains.");
            }

            public Task<GenerationDownloadTicket> CreateDownloadTicketAsync(
                string jobId,
                CancellationToken cancellationToken)
            {
                TicketCount++;
                return Task.FromResult(new GenerationDownloadTicket(
                    "media-verified",
                    Hash('9'),
                    "https://media.example.test/video.mp4",
                    TicketExpiresAtUtc,
                    "video/mp4",
                    4096));
            }
        }

        private sealed class RecordingMediaCache : IVerifiedMediaCache
        {
            public Func<GenerationDownloadTicket, CancellationToken, Task<VerifiedMediaHandle>>
                GetOrDownloadAsyncOverride { get; set; }
            public int AcquireCount { get; private set; }

            public Task<VerifiedMediaHandle> GetOrDownloadAsync(
                GenerationDownloadTicket ticket,
                CancellationToken cancellationToken)
            {
                AcquireCount++;
                if (GetOrDownloadAsyncOverride != null)
                    return GetOrDownloadAsyncOverride(ticket, cancellationToken);
                return Task.FromResult(new VerifiedMediaHandle(
                    ticket.ContentHash,
                    "C:/fixture/cache/" + ticket.ContentHash.Substring(7) + ".mp4",
                    ticket.Length));
            }
        }

        private sealed class RecordingPlaybackGateway : IMediaPlaybackGateway
        {
            public int GeneratedPlayCount { get; private set; }
            public int FallbackPlayCount { get; private set; }

            public Task<MediaPlaybackCompletion> PlayGeneratedAsync(
                VerifiedMediaHandle media,
                CancellationToken cancellationToken)
            {
                GeneratedPlayCount++;
                return Task.FromResult(MediaPlaybackCompletion.Completed);
            }

            public Task<MediaPlaybackCompletion> PlayFallbackAsync(
                string fallbackCueId,
                CancellationToken cancellationToken)
            {
                FallbackPlayCount++;
                return Task.FromResult(MediaPlaybackCompletion.Completed);
            }
        }

        private sealed class AdvancingClock : IGenerationFlowClock
        {
            public static readonly DateTimeOffset InitialUtc =
                new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

            public double MonotonicSeconds { get; private set; }
            public DateTimeOffset UtcNow => InitialUtc.AddSeconds(MonotonicSeconds);

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Poll intervals advance deterministically. A long delay is the coordinator's
                // total-deadline sentinel; keep it pending so an intentionally delayed fake
                // operation can complete first, just as it would with a real main-thread clock.
                if (delay.TotalSeconds > 1.000001d)
                {
                    var completion = new TaskCompletionSource<int>();
                    cancellationToken.Register(() => completion.TrySetCanceled());
                    return completion.Task;
                }
                MonotonicSeconds += Math.Max(0d, delay.TotalSeconds);
                return Task.FromResult(0);
            }
        }
    }
}
