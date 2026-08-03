using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchReportTests
{
    [Fact]
    public void ReportCountsAdoptionRetriesAndAllSettledAttemptCost()
    {
        GenerationBatch batch = BatchWithItems(
            Item(
                "shot.adopted",
                GenerationBatchItemStatus.Adopted,
                Attempt(1, durationMilliseconds: 5_000, actualCostMicros: 1_000_000),
                Attempt(2, durationMilliseconds: 6_000, actualCostMicros: 1_250_000)),
            Item(
                "shot.rejected",
                GenerationBatchItemStatus.Rejected,
                Attempt(1, durationMilliseconds: 8_000, actualCostMicros: 750_000)));

        GenerationBatchReport report = GenerationBatchReportCalculator.Calculate(batch);

        Assert.Equal(2, report.TotalItemCount);
        Assert.Equal(2, report.AttemptedItemCount);
        Assert.Equal(3, report.AttemptCount);
        Assert.Equal(1, report.RetryCount);
        Assert.Equal(2, report.ReviewedCount);
        Assert.Equal(1, report.AdoptedCount);
        Assert.Equal(1, report.RejectedCount);
        Assert.Equal(0.5m, report.AdoptionRate);
        Assert.Equal(0.5m, report.AverageRetryCount);
        Assert.Equal(3_000_000, report.ActualCostMicros);
        Assert.Equal(6_000, report.AdoptedDurationMilliseconds);
        Assert.Equal(500_000m, report.UnitCostPerFinishedSecondMicros);
        Assert.True(report.CostComplete);
        Assert.Equal("USD", report.Currency);
    }

    [Fact]
    public void NoHumanDecisionKeepsAdoptionAndUnitCostUndefined()
    {
        GenerationBatch batch = BatchWithItems(
            Item(
                "shot.pending",
                GenerationBatchItemStatus.AwaitingReview,
                Attempt(1, durationMilliseconds: 8_000, actualCostMicros: 0)));

        GenerationBatchReport report = GenerationBatchReportCalculator.Calculate(batch);

        Assert.Null(report.AdoptionRate);
        Assert.Null(report.UnitCostPerFinishedSecondMicros);
        Assert.Equal(0, report.AdoptedDurationMilliseconds);
        Assert.True(report.CostComplete);
    }

    [Fact]
    public void UnsettledAttemptMakesUnitCostIncompleteAndUndefined()
    {
        GenerationBatchAttempt pendingSettlement = Attempt(
            1,
            durationMilliseconds: 8_000,
            actualCostMicros: 400_000) with
        {
            CostSettled = false,
        };
        GenerationBatch batch = BatchWithItems(
            Item("shot.adopted", GenerationBatchItemStatus.Adopted, pendingSettlement));

        GenerationBatchReport report = GenerationBatchReportCalculator.Calculate(batch);

        Assert.False(report.CostComplete);
        Assert.Null(report.UnitCostPerFinishedSecondMicros);
        Assert.Equal(0, report.ActualCostMicros);
    }

    private static GenerationBatch BatchWithItems(params GenerationBatchItem[] items) =>
        new()
        {
            Id = Guid.Parse("d2cedab2-e0ea-4a1e-9bfe-77b674d87918"),
            IdempotencyKey = "batch.report",
            RequestFingerprint = BatchFixture.Hash("batch.report"),
            ManifestId = "manifest.report",
            ManifestContentHash = BatchFixture.Hash("manifest.report"),
            TargetTier = GenerationBatchTier.PreviewFast,
            Currency = "USD",
            Status = items.All(item => item.Status is GenerationBatchItemStatus.Adopted or GenerationBatchItemStatus.Rejected)
                ? GenerationBatchStatus.Reviewed
                : GenerationBatchStatus.AwaitingReview,
            Items = items,
            CreatedAtUtc = DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2026-07-16T00:01:00Z"),
        };

    private static GenerationBatchItem Item(
        string shotId,
        GenerationBatchItemStatus status,
        params GenerationBatchAttempt[] attempts) =>
        new()
        {
            ShotId = shotId,
            InputHash = BatchFixture.Hash("input:" + shotId),
            RequestedTier = GenerationBatchTier.FinalQuality,
            TargetTier = GenerationBatchTier.PreviewFast,
            TargetDurationMilliseconds = 8_000,
            AspectRatio = "16:9",
            MaximumCostMicros = 2_000_000,
            DialogueBinding = "bound_to_primary_media",
            FirstFrame = new GenerationBatchFramePin(
                "report-first-frame",
                "v1",
                BatchFixture.Hash("report-first-frame"),
                "image",
                "approved_frame"),
            LastFrame = new GenerationBatchFramePin(
                "report-last-frame",
                "v1",
                BatchFixture.Hash("report-last-frame"),
                "image",
                "approved_frame"),
            PrimaryMedia = new GenerationBatchMediaPin(
                "report-primary",
                "v1",
                BatchFixture.Hash("report-primary"),
                "video"),
            FallbackMedia = new GenerationBatchMediaPin(
                "report-fallback",
                "v1",
                BatchFixture.Hash("report-fallback"),
                "image"),
            Status = status,
            Attempts = attempts,
        };

    private static GenerationBatchAttempt Attempt(
        int number,
        int durationMilliseconds,
        long actualCostMicros) =>
        new(
            number,
            BatchFixture.DeterministicGuid("job:" + number + ":" + actualCostMicros),
            new GenerationBatchArtifact(
                "artifact-" + number + "-" + actualCostMicros,
                "video",
                BatchFixture.Hash("artifact:" + number + ":" + actualCostMicros)),
            durationMilliseconds,
            actualCostMicros,
            CostSettled: true,
            DateTimeOffset.Parse("2026-07-16T00:00:30Z").AddSeconds(number));
}
