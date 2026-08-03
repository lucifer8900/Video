using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Pure orchestration for the client generation loop. Unity, networking, disk I/O and video
    /// playback stay behind injected gateways so EditMode tests can exercise every path offline.
    /// </summary>
    public sealed class GenerationPlaybackCoordinator
    {
        private readonly IGenerationJobGateway _jobs;
        private readonly IVerifiedMediaCache _cache;
        private readonly IMediaPlaybackGateway _playback;
        private readonly IGenerationFlowClock _clock;
        private readonly object _registrationsGate = new object();
        private readonly Dictionary<string, Registration> _registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);

        public GenerationPlaybackCoordinator(
            IGenerationJobGateway jobs,
            IVerifiedMediaCache cache,
            IMediaPlaybackGateway playback,
            IGenerationFlowClock clock)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _playback = playback ?? throw new ArgumentNullException(nameof(playback));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Starts or joins one in-flight idempotent flow. Concurrent requests with the same key and
        /// hash share one task. Completed registrations are removed so later playback can replay
        /// the cached media without retaining every task for the lifetime of the game.
        /// </summary>
        public Task<GenerationPlaybackResult> RunAsync(GenerationPlaybackRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Registration registration;
            lock (_registrationsGate)
            {
                if (_registrations.TryGetValue(request.IdempotencyKey, out registration))
                {
                    if (string.Equals(registration.InputHash, request.InputHash, StringComparison.Ordinal))
                        return registration.Completion.Task;
                    return PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.IdempotencyConflict,
                        string.Empty);
                }

                registration = new Registration(request.InputHash);
                _registrations.Add(request.IdempotencyKey, registration);
            }

            // Execute outside the registration lock. Immediate fake gateways are allowed to run
            // synchronously; all subscribers still observe the same completion task.
            _ = CompleteRegistrationAsync(request, registration);
            return registration.Completion.Task;
        }

        private async Task CompleteRegistrationAsync(
            GenerationPlaybackRequest request,
            Registration registration)
        {
            GenerationPlaybackResult result;
            try
            {
                result = await ExecuteAsync(request);
            }
            catch (Exception)
            {
                // Adapters must map their expected failures, but a malformed or faulty adapter
                // can never strand the story. Do not expose exception messages or signed URLs.
                result = await PlayFallbackAsync(
                    request,
                    GenerationPlaybackFailure.InvalidResponse,
                    string.Empty);
            }
            lock (_registrationsGate)
            {
                if (_registrations.TryGetValue(request.IdempotencyKey, out Registration current) &&
                    ReferenceEquals(current, registration))
                {
                    _registrations.Remove(request.IdempotencyKey);
                }
            }
            registration.Completion.TrySetResult(result);
        }

        private async Task<GenerationPlaybackResult> ExecuteAsync(GenerationPlaybackRequest request)
        {
            if (!request.OnlineGenerationEnabled)
            {
                return await PlayFallbackAsync(
                    request,
                    GenerationPlaybackFailure.OnlineGenerationDisabled,
                    string.Empty);
            }

            double startedAt = _clock.MonotonicSeconds;
            if (!IsFinite(startedAt))
            {
                return await PlayFallbackAsync(
                    request,
                    GenerationPlaybackFailure.InvalidResponse,
                    string.Empty);
            }
            double deadline = startedAt + request.TotalTimeoutSeconds;
            string jobId = string.Empty;

            try
            {
                GenerationJobSnapshot job = await AwaitBeforeDeadlineAsync(
                    token => _jobs.CreateOrGetAsync(request, token),
                    deadline);
                EnsureJob(job, null);
                jobId = job.JobId;

                while (IsPending(job.State))
                {
                    await DelayForPollAsync(request.PollIntervalSeconds, deadline);
                    job = await AwaitBeforeDeadlineAsync(
                        token => _jobs.GetAsync(jobId, token),
                        deadline);
                    EnsureJob(job, jobId);
                }

                if (job.State == GenerationJobState.Failed)
                {
                    GenerationPlaybackFailure failure =
                        job.FailureCode.StartsWith("moderation.", StringComparison.OrdinalIgnoreCase)
                            ? GenerationPlaybackFailure.ModerationRejected
                            : GenerationPlaybackFailure.JobFailed;
                    return await PlayFallbackAsync(request, failure, jobId);
                }
                if (job.State == GenerationJobState.Expired)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.JobExpired,
                        jobId);
                }
                if (job.State != GenerationJobState.Ready)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.InvalidResponse,
                        jobId);
                }

                GenerationDownloadTicket ticket = await AwaitBeforeDeadlineAsync(
                    token => _jobs.CreateDownloadTicketAsync(jobId, token),
                    deadline);
                if (ticket == null)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.InvalidResponse,
                        jobId);
                }
                if (ticket.ExpiresAtUtc <= _clock.UtcNow)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.DownloadTicketExpired,
                        jobId);
                }

                VerifiedMediaHandle media;
                try
                {
                    media = await AwaitBeforeDeadlineAsync(
                        token => _cache.GetOrDownloadAsync(ticket, token),
                        deadline);
                }
                catch (GenerationTransportException)
                {
                    throw;
                }
                catch (TotalDeadlineException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.CacheFailure,
                        jobId);
                }

                if (media == null ||
                    !string.Equals(media.ContentHash, ticket.ContentHash, StringComparison.Ordinal) ||
                    media.Length != ticket.Length)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.CacheFailure,
                        jobId);
                }

                MediaPlaybackCompletion generatedCompletion;
                try
                {
                    Task<MediaPlaybackCompletion> playbackTask =
                        _playback.PlayGeneratedAsync(media, CancellationToken.None);
                    if (playbackTask == null) throw new InvalidOperationException();
                    generatedCompletion = await playbackTask;
                }
                catch (Exception)
                {
                    generatedCompletion = MediaPlaybackCompletion.Failed;
                }

                if (generatedCompletion != MediaPlaybackCompletion.Completed)
                {
                    return await PlayFallbackAsync(
                        request,
                        GenerationPlaybackFailure.GeneratedPlaybackFailure,
                        jobId);
                }

                return new GenerationPlaybackResult(
                    GenerationPlaybackOutcome.GeneratedMediaPlayed,
                    GenerationPlaybackFailure.None,
                    jobId,
                    generatedCompletion);
            }
            catch (TotalDeadlineException)
            {
                return await PlayFallbackAsync(
                    request,
                    GenerationPlaybackFailure.TotalTimeout,
                    jobId);
            }
            catch (GenerationTransportException exception)
            {
                return await PlayFallbackAsync(
                    request,
                    MapTransportFailure(exception.Failure),
                    jobId);
            }
            catch (Exception)
            {
                return await PlayFallbackAsync(
                    request,
                    GenerationPlaybackFailure.InvalidResponse,
                    jobId);
            }
        }

        private async Task<T> AwaitBeforeDeadlineAsync<T>(
            Func<CancellationToken, Task<T>> operationFactory,
            double deadline)
        {
            double remaining = RemainingSeconds(deadline);
            if (remaining <= 0d) throw new TotalDeadlineException();

            using (var operationCancellation = new CancellationTokenSource())
            {
                Task<T> operation = operationFactory(operationCancellation.Token);
                if (operation == null) throw new InvalidOperationException();
                if (operation.IsCompleted) return await operation;

                using (var deadlineCancellation = new CancellationTokenSource())
                {
                    Task deadlineTask = _clock.DelayAsync(
                        TimeSpan.FromSeconds(remaining),
                        deadlineCancellation.Token);
                    if (deadlineTask == null) throw new InvalidOperationException();

                    Task completed = await Task.WhenAny(operation, deadlineTask);
                    if (ReferenceEquals(completed, operation))
                    {
                        deadlineCancellation.Cancel();
                        return await operation;
                    }

                    operationCancellation.Cancel();
                    throw new TotalDeadlineException();
                }
            }
        }

        private async Task DelayForPollAsync(double pollIntervalSeconds, double deadline)
        {
            double remaining = RemainingSeconds(deadline);
            if (remaining <= 0d) throw new TotalDeadlineException();
            double delaySeconds = Math.Min(pollIntervalSeconds, remaining);
            Task delay = _clock.DelayAsync(
                TimeSpan.FromSeconds(delaySeconds),
                CancellationToken.None);
            if (delay == null) throw new InvalidOperationException();
            await delay;
            if (RemainingSeconds(deadline) <= 0d) throw new TotalDeadlineException();
        }

        private double RemainingSeconds(double deadline)
        {
            double current = _clock.MonotonicSeconds;
            if (!IsFinite(current)) return 0d;
            return deadline - current;
        }

        private async Task<GenerationPlaybackResult> PlayFallbackAsync(
            GenerationPlaybackRequest request,
            GenerationPlaybackFailure failure,
            string jobId)
        {
            MediaPlaybackCompletion completion;
            try
            {
                Task<MediaPlaybackCompletion> fallback =
                    _playback.PlayFallbackAsync(request.FallbackCueId, CancellationToken.None);
                if (fallback == null) throw new InvalidOperationException();
                completion = await fallback;
            }
            catch (Exception)
            {
                completion = MediaPlaybackCompletion.Failed;
            }

            GenerationPlaybackOutcome outcome = completion == MediaPlaybackCompletion.Completed
                ? GenerationPlaybackOutcome.FallbackPlayed
                : GenerationPlaybackOutcome.FallbackUnavailable;
            return new GenerationPlaybackResult(outcome, failure, jobId, completion);
        }

        private static void EnsureJob(GenerationJobSnapshot job, string expectedJobId)
        {
            if (job == null) throw new InvalidOperationException();
            if (!string.IsNullOrEmpty(expectedJobId) &&
                !string.Equals(job.JobId, expectedJobId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }
        }

        private static bool IsPending(GenerationJobState state)
        {
            return state == GenerationJobState.Created ||
                   state == GenerationJobState.Queued ||
                   state == GenerationJobState.Generating ||
                   state == GenerationJobState.Moderating ||
                   state == GenerationJobState.Transcoding;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static GenerationPlaybackFailure MapTransportFailure(
            GenerationTransportFailure failure)
        {
            switch (failure)
            {
                case GenerationTransportFailure.NetworkUnavailable:
                    return GenerationPlaybackFailure.NetworkUnavailable;
                case GenerationTransportFailure.ServerRejected:
                    return GenerationPlaybackFailure.ServerRejected;
                default:
                    return GenerationPlaybackFailure.InvalidResponse;
            }
        }

        private sealed class Registration
        {
            public Registration(string inputHash)
            {
                InputHash = inputHash;
                Completion = new TaskCompletionSource<GenerationPlaybackResult>();
            }

            public string InputHash { get; }
            public TaskCompletionSource<GenerationPlaybackResult> Completion { get; }
        }

        private sealed class TotalDeadlineException : Exception
        {
        }
    }
}
