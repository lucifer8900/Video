using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchServiceTests
{
    [Fact]
    public async Task DomainRejectsMoreThanTwentyShotsBeforeAnyWriteOrProviderCall()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new RecordingAttemptExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(21);

        GenerationBatchValidationException exception = await Assert.ThrowsAsync<GenerationBatchValidationException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest(
                    "batch.limit.21",
                    manifest,
                    manifest.Shots.Select(shot => shot.ShotId).ToArray()),
                CancellationToken.None));

        Assert.Equal("batch.shot_count", exception.Code);
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task ExactlyTwentyShotsRunAsMockFastPreviewsAndAwaitHumanReview()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new RecordingAttemptExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(20);

        GenerationBatch batch = await service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.limit.20",
                manifest,
                manifest.Shots.Select(shot => shot.ShotId).Reverse().ToArray()),
            CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.AwaitingReview, batch.Status);
        Assert.Equal(GenerationBatchTier.PreviewFast, batch.TargetTier);
        Assert.Equal(20, batch.Items.Count);
        Assert.All(batch.Items, item =>
        {
            Assert.Equal(GenerationBatchItemStatus.AwaitingReview, item.Status);
            Assert.Single(item.Attempts);
            Assert.Equal(0, item.Attempts[0].ActualCostMicros);
        });
        Assert.Equal(20, executor.CallCount);
        Assert.All(executor.Requests, request =>
        {
            GenerationBatchShot expected = manifest.Shots.Single(
                shot => string.Equals(shot.ShotId, request.ShotId, StringComparison.Ordinal));
            Assert.Equal(expected.DialogueBinding, request.DialogueBinding);
            Assert.Equal(expected.FirstFrame, request.FirstFrame);
            Assert.Equal(expected.LastFrame, request.LastFrame);
            Assert.Equal(expected.PrimaryMedia, request.PrimaryMedia);
            Assert.Equal(expected.FallbackMedia, request.FallbackMedia);
        });
        Assert.Equal(
            manifest.Shots.Select(shot => shot.ShotId).Order(StringComparer.Ordinal),
            executor.Requests.Select(request => request.ShotId));
    }

    [Fact]
    public async Task UndispatchableManifestFailsClosedBeforeRepositoryOrProvider()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new RecordingAttemptExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1) with
        {
            ApprovalStatus = GenerationBatchManifestApproval.NeedsReview,
            ShotDispatchAllowed = false,
        };

        GenerationBatchValidationException exception = await Assert.ThrowsAsync<GenerationBatchValidationException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest("batch.blocked", manifest, ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("manifest.not_dispatchable", exception.Code);
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task NonMockExecutorIsRejectedBeforeRepositoryOrProviderEvenWhenManifestIsApproved()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new RecordingAttemptExecutor(GenerationBatchProviderMode.External);
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);

        GenerationBatchGateException exception = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest("batch.external", manifest, ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("batch.external_provider_g2_blocked", exception.Code);
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task TerminalMockFailureIsPersistedAndDoesNotBlockANewBatch()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var failingService = new GenerationBatchService(
            repository,
            new ThrowingMockExecutor(),
            TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);

        GenerationBatch failed = await failingService.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.failure.one", manifest, ["shot.001"]),
            CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.Failed, failed.Status);
        Assert.Equal(GenerationBatchItemStatus.Failed, failed.Items[0].Status);
        Assert.Equal("batch.mock_execution_failed", failed.Items[0].FailureCode);

        var healthyService = new GenerationBatchService(
            repository,
            new RecordingAttemptExecutor(),
            TimeProvider.System);
        GenerationBatch recovered = await healthyService.StartPreviewAsync(
            new StartGenerationBatchRequest("batch.failure.two", manifest, ["shot.001"]),
            CancellationToken.None);
        Assert.Equal(GenerationBatchStatus.AwaitingReview, recovered.Status);
    }

    [Fact]
    public async Task TerminalFailureLeavesNoPendingOrUnreviewableItemsInTheBatch()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new FailOnSecondAttemptExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(3);

        GenerationBatch failed = await service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.failure.multi",
                manifest,
                manifest.Shots.Select(shot => shot.ShotId).ToArray()),
            CancellationToken.None);

        Assert.Equal(GenerationBatchStatus.Failed, failed.Status);
        Assert.All(failed.Items, item =>
        {
            Assert.Equal(GenerationBatchItemStatus.Failed, item.Status);
            Assert.NotNull(item.FailureCode);
        });
        Assert.Equal("batch.mock_execution_failed", failed.Items[1].FailureCode);
        Assert.Equal("batch.skipped_after_failure", failed.Items[0].FailureCode);
        Assert.Equal("batch.skipped_after_failure", failed.Items[2].FailureCode);
        Assert.Equal(2, executor.CallCount);
    }

    [Fact]
    public async Task ReturnedCollectionsCannotMutatePersistedBatchWithoutVersionChange()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var service = new GenerationBatchService(
            repository,
            new RecordingAttemptExecutor(),
            TimeProvider.System);
        GenerationBatch batch = await service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.immutable-return",
                BatchFixture.Dispatchable(1),
                ["shot.001"]),
            CancellationToken.None);

        if (batch.Items is GenerationBatchItem[] exposedItems)
        {
            exposedItems[0] = exposedItems[0] with
            {
                Status = GenerationBatchItemStatus.Adopted,
            };
        }
        else
        {
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GenerationBatchItem>)batch.Items)[0] = batch.Items[0]);
        }

        GenerationBatch persisted = (await repository.GetAsync(batch.Id, CancellationToken.None))!;
        Assert.Equal(GenerationBatchItemStatus.AwaitingReview, persisted.Items[0].Status);
        Assert.False(persisted.Items is GenerationBatchItem[]);
        Assert.False(persisted.Items[0].Attempts is GenerationBatchAttempt[]);
    }

    private sealed class RecordingAttemptExecutor : IGenerationBatchAttemptExecutor
    {
        public RecordingAttemptExecutor(GenerationBatchProviderMode mode = GenerationBatchProviderMode.Mock) =>
            Mode = mode;

        public GenerationBatchProviderMode Mode { get; }

        public List<GenerationBatchAttemptRequest> Requests { get; } = [];

        public int CallCount => Requests.Count;

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(new GenerationBatchAttemptReceipt(
                GenerationJobId: BatchFixture.DeterministicGuid(request.ShotId + request.AttemptNumber),
                Artifact: new GenerationBatchArtifact(
                    "mock-artifact-" + request.ShotId,
                    "video",
                    BatchFixture.Hash("artifact:" + request.ShotId)),
                ActualDurationMilliseconds: request.TargetDurationMilliseconds,
                ActualCostMicros: 0,
                CostSettled: true));
        }
    }

    private sealed class ThrowingMockExecutor : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive provider detail must not be persisted");
    }

    private sealed class FailOnSecondAttemptExecutor : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public int CallCount { get; private set; }

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 2) throw new InvalidOperationException("terminal mock failure");
            return Task.FromResult(new GenerationBatchAttemptReceipt(
                BatchFixture.DeterministicGuid(request.ShotId),
                new GenerationBatchArtifact(
                    "artifact-" + request.ShotId,
                    "video",
                    BatchFixture.Hash("artifact:" + request.ShotId)),
                request.TargetDurationMilliseconds,
                0,
                true));
        }
    }
}

internal static class BatchFixture
{
    public static GenerationBatchManifest Dispatchable(int count)
    {
        GenerationBatchShot[] shots = Enumerable.Range(1, count)
            .Select(index => new GenerationBatchShot(
                $"shot.{index:000}",
                Hash($"input:{index}"),
                DispatchAllowed: true,
                ReadinessStatus: GenerationBatchShotReadiness.Ready,
                RequestedTier: GenerationBatchTier.FinalQuality,
                TargetDurationMilliseconds: 8_000,
                AspectRatio: "16:9",
                MaximumCostMicros: 1_000_000,
                DialogueBinding: "bound_to_primary_media",
                FirstFrame: Frame($"first-frame:{index}"),
                LastFrame: Frame($"last-frame:{index}"),
                PrimaryMedia: Media($"primary:{index}", "video"),
                FallbackMedia: Media($"fallback:{index}", "image")))
            .ToArray();

        return new GenerationBatchManifest(
            "manifest.synthetic.approved",
            Hash("manifest.synthetic.approved"),
            GenerationBatchManifestApproval.Approved,
            ShotDispatchAllowed: true,
            Currency: "USD",
            shots);
    }

    public static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    public static Guid DeterministicGuid(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static GenerationBatchFramePin Frame(string identity) =>
        new(
            identity.Replace(':', '-'),
            "v1",
            Hash(identity),
            "image",
            "approved_frame");

    private static GenerationBatchMediaPin Media(string identity, string mediaType) =>
        new(identity.Replace(':', '-'), "v1", Hash(identity), mediaType);

    public static GenerationBatchItemReview Review(
        GenerationBatch batch,
        string shotId,
        GenerationBatchReviewDecision decision)
    {
        GenerationBatchItem item = batch.Items.Single(
            candidate => string.Equals(candidate.ShotId, shotId, StringComparison.Ordinal));
        GenerationBatchAttempt attempt = item.Attempts[^1];
        return new GenerationBatchItemReview(
            shotId,
            decision,
            attempt.AttemptNumber,
            attempt.Artifact.ContentHash);
    }
}
