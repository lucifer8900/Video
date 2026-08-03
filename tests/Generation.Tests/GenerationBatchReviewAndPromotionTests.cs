using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchReviewAndPromotionTests
{
    [Fact]
    public async Task AnotherBatchCannotStartUntilEveryPreviewHasHumanDecision()
    {
        var harness = new ReviewHarness();
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(2);
        GenerationBatch first = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.review.first", manifest, ["shot.001", "shot.002"]),
            CancellationToken.None);

        GenerationBatch afterPartialReview = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.partial",
                first.Id,
                [BatchFixture.Review(first, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.AwaitingReview, afterPartialReview.Status);
        GenerationBatchGateException blocked = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.StartPreviewAsync(
                new StartGenerationBatchRequest("batch.review.second", manifest, ["shot.001"]),
                CancellationToken.None));
        Assert.Equal("batch.previous_review_pending", blocked.Code);

        GenerationBatch reviewed = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.complete",
                first.Id,
                [BatchFixture.Review(afterPartialReview, "shot.002", GenerationBatchReviewDecision.Reject)]),
            CancellationToken.None);
        Assert.Equal(GenerationBatchStatus.Reviewed, reviewed.Status);

        GenerationBatch second = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.review.second", manifest, ["shot.001"]),
            CancellationToken.None);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task ChangingManifestHashCannotBypassThePreviousReviewGateForTheSameManifestId()
    {
        var harness = new ReviewHarness();
        GenerationBatchManifest firstManifest = BatchFixture.Dispatchable(1);
        await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.manifest-lane.first",
                firstManifest,
                ["shot.001"]),
            CancellationToken.None);
        GenerationBatchManifest revisedContent = firstManifest with
        {
            ContentHash = BatchFixture.Hash("manifest.synthetic.approved.revision-2"),
            Shots =
            [
                firstManifest.Shots[0] with
                {
                    InputHash = BatchFixture.Hash("input.revision-2"),
                },
            ],
        };

        GenerationBatchGateException blocked = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.StartPreviewAsync(
                new StartGenerationBatchRequest(
                    "batch.manifest-lane.second",
                    revisedContent,
                    ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("batch.previous_review_pending", blocked.Code);
        Assert.Equal(1, harness.Executor.CallCount);
    }

    [Fact]
    public async Task OnlyAdoptedFinalQualityShotCanBePromoted()
    {
        var harness = new ReviewHarness();
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(2);
        GenerationBatch preview = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.promote.preview", manifest, ["shot.001", "shot.002"]),
            CancellationToken.None);
        GenerationBatch reviewed = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.promote",
                preview.Id,
                [
                    BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Adopt),
                    BatchFixture.Review(preview, "shot.002", GenerationBatchReviewDecision.Reject),
                ]),
            CancellationToken.None);

        GenerationBatch final = await harness.Service.PromoteAdoptedAsync(
            new PromoteGenerationBatchRequest("batch.promote.final", reviewed.Id, ["shot.001"]),
            CancellationToken.None);

        Assert.Equal(GenerationBatchTier.FinalQuality, final.TargetTier);
        Assert.Equal(reviewed.Id, final.SourcePreviewBatchId);
        Assert.Equal(GenerationBatchStatus.AwaitingReview, final.Status);
        Assert.Single(final.Items);
        Assert.Equal(GenerationBatchTier.FinalQuality, final.Items[0].TargetTier);
        Assert.Equal(GenerationBatchTier.FinalQuality, harness.Executor.Requests[^1].Tier);

        GenerationBatchGateException rejected = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.PromoteAdoptedAsync(
                new PromoteGenerationBatchRequest("batch.promote.rejected", reviewed.Id, ["shot.002"]),
                CancellationToken.None));
        Assert.Equal("batch.promotion_not_adopted", rejected.Code);
    }

    [Fact]
    public async Task RetryCreatesSecondCreativeAttemptAndReturnsToReview()
    {
        var harness = new ReviewHarness();
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);
        GenerationBatch preview = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.retry", manifest, ["shot.001"]),
            CancellationToken.None);

        GenerationBatch retried = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.retry",
                preview.Id,
                [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Retry)]),
            CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.AwaitingReview, retried.Status);
        Assert.Equal(GenerationBatchItemStatus.AwaitingReview, retried.Items[0].Status);
        Assert.Equal(2, retried.Items[0].Attempts.Count);
        Assert.Equal([1, 2], retried.Items[0].Attempts.Select(attempt => attempt.AttemptNumber));
        Assert.Equal(2, harness.Executor.CallCount);
    }

    [Fact]
    public async Task PreviewFastOnlyShotCannotBePromotedEvenAfterAdoption()
    {
        var harness = new ReviewHarness();
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1) with
        {
            Shots =
            [
                BatchFixture.Dispatchable(1).Shots[0] with
                {
                    RequestedTier = GenerationBatchTier.PreviewFast,
                },
            ],
        };
        GenerationBatch preview = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.preview-only", manifest, ["shot.001"]),
            CancellationToken.None);
        GenerationBatch reviewed = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.preview-only",
                preview.Id,
                [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);

        GenerationBatchGateException exception = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.PromoteAdoptedAsync(
                new PromoteGenerationBatchRequest("batch.preview-only.final", reviewed.Id, ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("batch.promotion_tier", exception.Code);
    }

    [Fact]
    public async Task HumanReviewIsPinnedToTheAttemptAndArtifactActuallyViewed()
    {
        var harness = new ReviewHarness();
        GenerationBatch preview = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.stale-review",
                BatchFixture.Dispatchable(1),
                ["shot.001"]),
            CancellationToken.None);
        GenerationBatchAttempt firstAttempt = preview.Items[0].Attempts[0];
        GenerationBatch retried = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.stale.retry",
                preview.Id,
                [
                    new GenerationBatchItemReview(
                        "shot.001",
                        GenerationBatchReviewDecision.Retry,
                        ExpectedAttemptNumber: firstAttempt.AttemptNumber,
                        ExpectedArtifactHash: firstAttempt.Artifact.ContentHash),
                ]),
            CancellationToken.None);

        GenerationBatchGateException stale = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.ReviewAsync(
                new ReviewGenerationBatchRequest(
                    "review.stale.adopt",
                    preview.Id,
                    [
                        new GenerationBatchItemReview(
                            "shot.001",
                            GenerationBatchReviewDecision.Adopt,
                            ExpectedAttemptNumber: firstAttempt.AttemptNumber,
                            ExpectedArtifactHash: firstAttempt.Artifact.ContentHash),
                    ]),
                CancellationToken.None));

        Assert.Equal("batch.review_stale_attempt", stale.Code);
        GenerationBatchAttempt latest = retried.Items[0].Attempts[^1];
        GenerationBatch adopted = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.current.adopt",
                preview.Id,
                [
                    new GenerationBatchItemReview(
                        "shot.001",
                        GenerationBatchReviewDecision.Adopt,
                        ExpectedAttemptNumber: latest.AttemptNumber,
                        ExpectedArtifactHash: latest.Artifact.ContentHash),
                ]),
            CancellationToken.None);
        Assert.Equal(GenerationBatchStatus.Reviewed, adopted.Status);
    }

    [Fact]
    public async Task RetryStartingDuringPreviousLeaseReleaseUsesANewRunGeneration()
    {
        var repository = new BlockingFirstReleaseRepository();
        var executor = new ReviewRecordingExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        var startRequest = new StartGenerationBatchRequest(
            "batch.run-generation",
            BatchFixture.Dispatchable(1),
            ["shot.001"]);

        Task<GenerationBatch> initialTask = service.StartPreviewAsync(
            startRequest,
            CancellationToken.None);
        await repository.FirstReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GenerationBatch awaitingReview = (await repository.GetAsync(
            repository.SingleBatchId,
            CancellationToken.None))!;
        Task<GenerationBatch> retryTask = service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.run-generation.retry",
                awaitingReview.Id,
                [BatchFixture.Review(
                    awaitingReview,
                    "shot.001",
                    GenerationBatchReviewDecision.Retry)]),
            CancellationToken.None);
        repository.AllowFirstRelease.TrySetResult();

        await initialTask;
        GenerationBatch retried = await retryTask;

        Assert.Equal(GenerationBatchStatus.AwaitingReview, retried.Status);
        Assert.Equal(2, retried.Items[0].Attempts.Count);
        Assert.Equal(2, executor.CallCount);
    }

    [Fact]
    public async Task AdoptedPreviewCannotBePromotedTwiceUnderDifferentIdempotencyKeys()
    {
        var harness = new ReviewHarness();
        GenerationBatch preview = await harness.Service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.single-promotion.preview",
                BatchFixture.Dispatchable(1),
                ["shot.001"]),
            CancellationToken.None);
        GenerationBatch reviewedPreview = await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.single-promotion.preview",
                preview.Id,
                [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);
        GenerationBatch firstFinal = await harness.Service.PromoteAdoptedAsync(
            new PromoteGenerationBatchRequest(
                "batch.single-promotion.final.one",
                reviewedPreview.Id,
                ["shot.001"]),
            CancellationToken.None);
        await harness.Service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "review.single-promotion.final",
                firstFinal.Id,
                [BatchFixture.Review(firstFinal, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);

        GenerationBatchGateException duplicate = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => harness.Service.PromoteAdoptedAsync(
                new PromoteGenerationBatchRequest(
                    "batch.single-promotion.final.two",
                    reviewedPreview.Id,
                    ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("batch.promotion_already_exists", duplicate.Code);
        Assert.Equal(2, harness.Executor.CallCount);
    }

    private sealed class ReviewHarness
    {
        public ReviewHarness()
        {
            Repository = new InMemoryGenerationBatchRepository();
            Executor = new ReviewRecordingExecutor();
            Service = new GenerationBatchService(Repository, Executor, TimeProvider.System);
        }

        public InMemoryGenerationBatchRepository Repository { get; }

        public ReviewRecordingExecutor Executor { get; }

        public GenerationBatchService Service { get; }
    }

    private sealed class ReviewRecordingExecutor : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public List<GenerationBatchAttemptRequest> Requests { get; } = [];

        public int CallCount => Requests.Count;

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            string identity = $"{request.BatchId:D}:{request.ShotId}:{request.Tier}:{request.AttemptNumber}";
            return Task.FromResult(new GenerationBatchAttemptReceipt(
                BatchFixture.DeterministicGuid(identity),
                new GenerationBatchArtifact(
                    "mock-artifact-" + BatchFixture.Hash(identity)[..23],
                    "video",
                    BatchFixture.Hash("artifact:" + identity)),
                request.TargetDurationMilliseconds,
                ActualCostMicros: 0,
                CostSettled: true));
        }
    }

    private sealed class BlockingFirstReleaseRepository : IGenerationBatchRepository
    {
        private readonly InMemoryGenerationBatchRepository _inner = new();
        private int _releaseCount;

        public TaskCompletionSource FirstReleaseEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid SingleBatchId { get; private set; }

        public async Task<GenerationBatchCreateResult> CreateOrGetAsync(
            GenerationBatchSubmission submission,
            Guid batchId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            GenerationBatchCreateResult result = await _inner.CreateOrGetAsync(
                submission,
                batchId,
                nowUtc,
                cancellationToken);
            SingleBatchId = result.Batch.Id;
            return result;
        }

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

        public async Task ReleaseRunAsync(
            Guid batchId,
            string owner,
            long fencingToken,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _releaseCount) == 1)
            {
                FirstReleaseEntered.TrySetResult();
                await AllowFirstRelease.Task.WaitAsync(cancellationToken);
            }

            await _inner.ReleaseRunAsync(batchId, owner, fencingToken, cancellationToken);
        }

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

        public Task<GenerationBatch> FailItemAsync(
            Guid batchId,
            long expectedVersion,
            string shotId,
            string failureCode,
            string runLeaseOwner,
            long runLeaseFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            _inner.FailItemAsync(
                batchId,
                expectedVersion,
                shotId,
                failureCode,
                runLeaseOwner,
                runLeaseFencingToken,
                nowUtc,
                cancellationToken);
    }
}
