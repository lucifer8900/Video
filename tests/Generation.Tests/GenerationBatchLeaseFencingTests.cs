using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchLeaseFencingTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExpiredOwnerCannotAppendAfterAnotherRunnerTakesTheLease()
    {
        var repository = new InMemoryGenerationBatchRepository();
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission(),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        GenerationBatchRunLease? first = await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.old",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatchRunLease firstLease = Assert.IsType<GenerationBatchRunLease>(first);
        GenerationBatchRunLease? replacement = await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.old",
            FixedNow.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatchAttempt attempt = Attempt();

        GenerationBatchGateException stale = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => repository.AppendAttemptAsync(
                created.Batch.Id,
                created.Batch.Version,
                "shot.001",
                attempt,
                runLeaseOwner: "runner.old",
                runLeaseFencingToken: firstLease.FencingToken,
                nowUtc: FixedNow.AddMinutes(6),
                CancellationToken.None));
        await repository.ReleaseRunAsync(
            created.Batch.Id,
            "runner.old",
            firstLease.FencingToken,
            CancellationToken.None);
        GenerationBatchRunLease? incorrectlyReleased = await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.other",
            FixedNow.AddMinutes(7),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatch appended = await repository.AppendAttemptAsync(
            created.Batch.Id,
            created.Batch.Version,
            "shot.001",
            attempt,
            runLeaseOwner: "runner.old",
            runLeaseFencingToken: replacement!.FencingToken,
            nowUtc: FixedNow.AddMinutes(6),
            CancellationToken.None);

        Assert.NotNull(replacement);
        Assert.True(replacement.FencingToken > firstLease.FencingToken);
        Assert.Null(incorrectlyReleased);
        Assert.Equal("batch.run_lease_lost", stale.Code);
        Assert.Equal(GenerationBatchStatus.AwaitingReview, appended.Status);
    }

    [Fact]
    public async Task CurrentOwnerCanRenewBeforeStartingTheNextShot()
    {
        var repository = new InMemoryGenerationBatchRepository();
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission(),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.same",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        GenerationBatchRunLease? renewed = await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.same",
            FixedNow.AddMinutes(4),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatchRunLease? blocked = await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.other",
            FixedNow.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.NotNull(renewed);
        Assert.Equal(FixedNow.AddMinutes(9), renewed.ExpiresAtUtc);
        Assert.Null(blocked);
    }

    private static GenerationBatchSubmission PendingSubmission()
    {
        GenerationBatchShot shot = BatchFixture.Dispatchable(1).Shots[0];
        return new GenerationBatchSubmission(
            "batch.lease.fencing",
            BatchFixture.Hash("batch.lease.fencing"),
            "manifest.lease",
            BatchFixture.Hash("manifest.lease"),
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

    private static GenerationBatchAttempt Attempt() =>
        new(
            1,
            Guid.NewGuid(),
            new GenerationBatchArtifact(
                "artifact.lease",
                "video",
                BatchFixture.Hash("artifact.lease")),
            8_000,
            0,
            CostSettled: true,
            FixedNow.AddMinutes(6));
}
