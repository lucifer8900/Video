using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchRepositoryContractTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryCreateRejectsInvalidPinnedSpec()
    {
        var repository = new InMemoryGenerationBatchRepository();
        GenerationBatchSubmission valid = PendingSubmission();
        GenerationBatchItem validItem = valid.Items[0];
        GenerationBatchSubmission invalid = valid with
        {
            Items =
            [
                validItem with
                {
                    FallbackMedia = validItem.FallbackMedia with { MediaType = "video/mp4" },
                },
            ],
        };

        GenerationBatchValidationException exception =
            await Assert.ThrowsAsync<GenerationBatchValidationException>(
                () => repository.CreateOrGetAsync(
                    invalid,
                    Guid.NewGuid(),
                    FixedNow,
                    CancellationToken.None));

        Assert.Equal("batch.persisted_spec_invalid", exception.Code);
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InMemoryAppendRejectsInvalidAttemptWithoutMutation()
    {
        var repository = new InMemoryGenerationBatchRepository();
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission(),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        GenerationBatchRunLease lease = (await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.contract",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None))!;
        var invalidAttempt = new GenerationBatchAttempt(
            1,
            Guid.NewGuid(),
            new GenerationBatchArtifact(
                "artifact.contract",
                "video/mp4",
                BatchFixture.Hash("artifact.contract")),
            8_000,
            0,
            CostSettled: true,
            FixedNow);

        GenerationBatchValidationException exception =
            await Assert.ThrowsAsync<GenerationBatchValidationException>(
                () => repository.AppendAttemptAsync(
                    created.Batch.Id,
                    created.Batch.Version,
                    "shot.001",
                    invalidAttempt,
                    lease.Owner,
                    lease.FencingToken,
                    FixedNow,
                    CancellationToken.None));
        GenerationBatch unchanged = (await repository.GetAsync(
            created.Batch.Id,
            CancellationToken.None))!;

        Assert.Equal("batch.persisted_spec_invalid", exception.Code);
        Assert.Equal(GenerationBatchStatus.Running, unchanged.Status);
        Assert.Empty(unchanged.Items[0].Attempts);
    }

    private static GenerationBatchSubmission PendingSubmission()
    {
        GenerationBatchShot shot = BatchFixture.Dispatchable(1).Shots[0];
        return new GenerationBatchSubmission(
            "batch.repository.contract",
            BatchFixture.Hash("batch.repository.contract"),
            "manifest.repository.contract",
            BatchFixture.Hash("manifest.repository.contract"),
            GenerationBatchTier.PreviewFast,
            SourcePreviewBatchId: null,
            "USD",
            [
                new GenerationBatchItem
                {
                    ShotId = shot.ShotId,
                    InputHash = shot.InputHash,
                    RequestedTier = shot.RequestedTier,
                    TargetTier = GenerationBatchTier.PreviewFast,
                    TargetDurationMilliseconds = shot.TargetDurationMilliseconds,
                    AspectRatio = shot.AspectRatio,
                    MaximumCostMicros = shot.MaximumCostMicros,
                    DialogueBinding = shot.DialogueBinding,
                    FirstFrame = shot.FirstFrame,
                    LastFrame = shot.LastFrame,
                    PrimaryMedia = shot.PrimaryMedia,
                    FallbackMedia = shot.FallbackMedia,
                    Status = GenerationBatchItemStatus.Pending,
                    Attempts = [],
                },
            ]);
    }
}
