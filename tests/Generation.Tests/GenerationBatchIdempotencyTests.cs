using System.Collections.Concurrent;
using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchIdempotencyTests
{
    [Fact]
    public async Task SequentialReplayReturnsSameBatchWithoutRepeatingAttempt()
    {
        var executor = new ConcurrentRecordingAttemptExecutor();
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);
        var request = new StartGenerationBatchRequest("batch.replay", manifest, ["shot.001"]);

        GenerationBatch first = await service.StartPreviewAsync(request, CancellationToken.None);
        GenerationBatch replay = await service.StartPreviewAsync(request, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.RequestFingerprint, replay.RequestFingerprint);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ShotOrderDoesNotChangeIdempotencyFingerprint()
    {
        var executor = new ConcurrentRecordingAttemptExecutor();
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(2);

        GenerationBatch first = await service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.order", manifest, ["shot.002", "shot.001"]),
            CancellationToken.None);
        GenerationBatch replay = await service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.order", manifest, ["shot.001", "shot.002"]),
            CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(2, executor.CallCount);
    }

    [Fact]
    public async Task ReusingKeyForDifferentShotSetFailsWithoutNewAttempt()
    {
        var executor = new ConcurrentRecordingAttemptExecutor();
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(2);
        await service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.conflict", manifest, ["shot.001"]),
            CancellationToken.None);

        await Assert.ThrowsAsync<GenerationBatchIdempotencyConflictException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest("batch.conflict", manifest, ["shot.002"]),
                CancellationToken.None));

        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task FingerprintIncludesEveryServerResolvedExecutionAndCostField()
    {
        var executor = new ConcurrentRecordingAttemptExecutor();
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        GenerationBatchManifest original = BatchFixture.Dispatchable(1);
        await service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.full-fingerprint", original, ["shot.001"]),
            CancellationToken.None);
        GenerationBatchManifest forgedSameHash = original with
        {
            Currency = "CNY",
            Shots =
            [
                original.Shots[0] with
                {
                    RequestedTier = GenerationBatchTier.PreviewFast,
                    TargetDurationMilliseconds = 9_000,
                    AspectRatio = "9:16",
                    MaximumCostMicros = 2_000_000,
                },
            ],
        };

        await Assert.ThrowsAsync<GenerationBatchIdempotencyConflictException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest(
                    "batch.full-fingerprint",
                    forgedSameHash,
                    ["shot.001"]),
                CancellationToken.None));

        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task FingerprintIncludesEveryPinnedMediaContentHash()
    {
        GenerationBatchManifest original = BatchFixture.Dispatchable(1);
        GenerationBatchShot shot = original.Shots[0];
        GenerationBatchManifest[] revisions =
        [
            original with
            {
                Shots =
                [
                    shot with
                    {
                        FirstFrame = shot.FirstFrame! with
                        {
                            ContentHash = BatchFixture.Hash("revised.first-frame"),
                        },
                    },
                ],
            },
            original with
            {
                Shots =
                [
                    shot with
                    {
                        LastFrame = shot.LastFrame! with
                        {
                            ContentHash = BatchFixture.Hash("revised.last-frame"),
                        },
                    },
                ],
            },
            original with
            {
                Shots =
                [
                    shot with
                    {
                        PrimaryMedia = shot.PrimaryMedia! with
                        {
                            ContentHash = BatchFixture.Hash("revised.primary-media"),
                        },
                    },
                ],
            },
            original with
            {
                Shots =
                [
                    shot with
                    {
                        FallbackMedia = shot.FallbackMedia with
                        {
                            ContentHash = BatchFixture.Hash("revised.fallback-media"),
                        },
                    },
                ],
            },
        ];

        for (int index = 0; index < revisions.Length; index++)
        {
            var executor = new ConcurrentRecordingAttemptExecutor();
            var service = new GenerationBatchService(
                new InMemoryGenerationBatchRepository(),
                executor,
                TimeProvider.System);
            string key = $"batch.pin-fingerprint.{index}";
            await service.StartPreviewAsync(
                new StartGenerationBatchRequest(key, original, ["shot.001"]),
                CancellationToken.None);

            await Assert.ThrowsAsync<GenerationBatchIdempotencyConflictException>(
                () => service.StartPreviewAsync(
                    new StartGenerationBatchRequest(key, revisions[index], ["shot.001"]),
                    CancellationToken.None));

            Assert.Equal(1, executor.CallCount);
        }
    }

    [Fact]
    public async Task ConcurrentReplayExecutesEachShotOnce()
    {
        var executor = new ConcurrentRecordingAttemptExecutor(TimeSpan.FromMilliseconds(50));
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);
        var request = new StartGenerationBatchRequest("batch.concurrent", manifest, ["shot.001"]);

        GenerationBatch[] results = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => service.StartPreviewAsync(request, CancellationToken.None)));

        Assert.Single(results.Select(result => result.Id).Distinct());
        Assert.All(results, result => Assert.Equal(GenerationBatchStatus.AwaitingReview, result.Status));
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ConcurrentReplayAcrossServiceInstancesExecutesEachShotOnce()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new ConcurrentRecordingAttemptExecutor(TimeSpan.FromMilliseconds(50));
        var firstService = new GenerationBatchService(repository, executor, TimeProvider.System);
        var secondService = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);
        var request = new StartGenerationBatchRequest(
            "batch.concurrent.cross-service",
            manifest,
            ["shot.001"]);

        GenerationBatch[] results = await Task.WhenAll(
            firstService.StartPreviewAsync(request, CancellationToken.None),
            secondService.StartPreviewAsync(request, CancellationToken.None));

        Assert.Single(results.Select(result => result.Id).Distinct());
        Assert.All(results, result => Assert.Equal(GenerationBatchStatus.AwaitingReview, result.Status));
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task CancellingFirstCallerDoesNotCancelAnotherIdempotentWaiter()
    {
        var executor = new ConcurrentRecordingAttemptExecutor(TimeSpan.FromMilliseconds(100));
        var service = new GenerationBatchService(
            new InMemoryGenerationBatchRepository(),
            executor,
            TimeProvider.System);
        var request = new StartGenerationBatchRequest(
            "batch.concurrent.cancellation",
            BatchFixture.Dispatchable(1),
            ["shot.001"]);
        using var firstCancellation = new CancellationTokenSource();

        Task<GenerationBatch> first = service.StartPreviewAsync(request, firstCancellation.Token);
        await Task.Delay(10);
        Task<GenerationBatch> second = service.StartPreviewAsync(request, CancellationToken.None);
        firstCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        GenerationBatch completed = await second;
        Assert.Equal(GenerationBatchStatus.AwaitingReview, completed.Status);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task CancelledOnlyWaiterCannotLeaveAFaultedSingleFlightCachedForever()
    {
        var repository = new TransientFailItemRepository();
        var executor = new ThrowOnceThenSucceedExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        var request = new StartGenerationBatchRequest(
            "batch.concurrent.fault-cleanup",
            BatchFixture.Dispatchable(1),
            ["shot.001"]);
        using var cancellation = new CancellationTokenSource();

        Task<GenerationBatch> abandonedWait = service.StartPreviewAsync(request, cancellation.Token);
        await executor.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandonedWait);
        await repository.FailItemReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(20);

        GenerationBatch replay = await service.StartPreviewAsync(request, CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.AwaitingReview, replay.Status);
        Assert.Equal(2, executor.CallCount);
    }

    private sealed class ConcurrentRecordingAttemptExecutor : IGenerationBatchAttemptExecutor
    {
        private readonly TimeSpan _delay;
        private int _callCount;

        public ConcurrentRecordingAttemptExecutor(TimeSpan delay = default) => _delay = delay;

        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public int CallCount => Volatile.Read(ref _callCount);

        public ConcurrentQueue<GenerationBatchAttemptRequest> Requests { get; } = new();

        public async Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Requests.Enqueue(request);
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken);

            return new GenerationBatchAttemptReceipt(
                BatchFixture.DeterministicGuid(request.ShotId + request.AttemptNumber),
                new GenerationBatchArtifact(
                    "mock-artifact-" + request.ShotId,
                    "video",
                    BatchFixture.Hash("artifact:" + request.ShotId)),
                request.TargetDurationMilliseconds,
                ActualCostMicros: 0,
                CostSettled: true);
        }
    }

    private sealed class ThrowOnceThenSucceedExecutor : IGenerationBatchAttemptExecutor
    {
        private int _callCount;

        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                FirstCallStarted.TrySetResult();
                await Task.Delay(50, cancellationToken);
                throw new InvalidOperationException("first deterministic failure");
            }

            return new GenerationBatchAttemptReceipt(
                BatchFixture.DeterministicGuid(request.ShotId + call),
                new GenerationBatchArtifact(
                    "artifact-replay",
                    "video",
                    BatchFixture.Hash("artifact-replay")),
                request.TargetDurationMilliseconds,
                0,
                true);
        }
    }

    private sealed class TransientFailItemRepository : IGenerationBatchRepository
    {
        private readonly InMemoryGenerationBatchRepository _inner = new();
        private int _failCalls;

        public TaskCompletionSource FailItemReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GenerationBatchCreateResult> CreateOrGetAsync(
            GenerationBatchSubmission submission,
            Guid batchId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            _inner.CreateOrGetAsync(submission, batchId, nowUtc, cancellationToken);

        public Task<GenerationBatch?> GetAsync(Guid batchId, CancellationToken cancellationToken) =>
            _inner.GetAsync(batchId, cancellationToken);

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            _inner.CountAsync(cancellationToken);

        public Task<GenerationBatchRunLease?> TryAcquireRunAsync(
            Guid batchId,
            string owner,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            _inner.TryAcquireRunAsync(batchId, owner, nowUtc, leaseDuration, cancellationToken);

        public Task ReleaseRunAsync(
            Guid batchId,
            string owner,
            long fencingToken,
            CancellationToken cancellationToken) =>
            _inner.ReleaseRunAsync(batchId, owner, fencingToken, cancellationToken);

        public Task<GenerationBatch> AppendAttemptAsync(
            Guid batchId,
            long expectedVersion,
            string shotId,
            GenerationBatchAttempt attempt,
            string runLeaseOwner,
            long runLeaseFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            _inner.AppendAttemptAsync(
                batchId,
                expectedVersion,
                shotId,
                attempt,
                runLeaseOwner,
                runLeaseFencingToken,
                nowUtc,
                cancellationToken);

        public Task<GenerationBatch> FailItemAsync(
            Guid batchId,
            long expectedVersion,
            string shotId,
            string failureCode,
            string runLeaseOwner,
            long runLeaseFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _failCalls) == 1)
            {
                FailItemReached.TrySetResult();
                throw new InvalidOperationException("transient repository failure");
            }

            return _inner.FailItemAsync(
                batchId,
                expectedVersion,
                shotId,
                failureCode,
                runLeaseOwner,
                runLeaseFencingToken,
                nowUtc,
                cancellationToken);
        }

        public Task<GenerationBatchReviewApplyResult> ApplyReviewsAsync(
            Guid batchId,
            long expectedVersion,
            string idempotencyKey,
            string reviewFingerprint,
            IReadOnlyList<GenerationBatchItemReview> reviews,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            _inner.ApplyReviewsAsync(
                batchId,
                expectedVersion,
                idempotencyKey,
                reviewFingerprint,
                reviews,
                nowUtc,
                cancellationToken);
    }
}
