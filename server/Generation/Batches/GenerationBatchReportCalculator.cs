namespace Lingmai.RedMist.Generation.Batches;

public sealed record GenerationBatchReport(
    int TotalItemCount,
    int AttemptedItemCount,
    int AttemptCount,
    int RetryCount,
    int ReviewedCount,
    int AdoptedCount,
    int RejectedCount,
    decimal? AdoptionRate,
    decimal AverageRetryCount,
    long ActualCostMicros,
    long AdoptedDurationMilliseconds,
    decimal? UnitCostPerFinishedSecondMicros,
    bool CostComplete,
    string Currency);

public static class GenerationBatchReportCalculator
{
    public static GenerationBatchReport Calculate(GenerationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (string.IsNullOrWhiteSpace(batch.Currency))
            throw Invalid("batch.report_currency", "The report currency is required.");

        int attemptedItemCount = batch.Items.Count(item => item.Attempts.Count > 0);
        int attemptCount = 0;
        int retryCount = 0;
        int adoptedCount = 0;
        int rejectedCount = 0;
        long actualCostMicros = 0;
        long adoptedDurationMilliseconds = 0;
        bool costComplete = true;

        foreach (GenerationBatchItem item in batch.Items)
        {
            attemptCount = checked(attemptCount + item.Attempts.Count);
            retryCount = checked(retryCount + Math.Max(item.Attempts.Count - 1, 0));
            adoptedCount += item.Status == GenerationBatchItemStatus.Adopted ? 1 : 0;
            rejectedCount += item.Status == GenerationBatchItemStatus.Rejected ? 1 : 0;

            foreach (GenerationBatchAttempt attempt in item.Attempts)
            {
                if (attempt.ActualCostMicros < 0 || attempt.ActualDurationMilliseconds <= 0)
                    throw Invalid("batch.report_attempt", "An attempt has invalid cost or duration.");

                if (attempt.CostSettled)
                    actualCostMicros = checked(actualCostMicros + attempt.ActualCostMicros);
                else
                    costComplete = false;
            }

            if (item.Status == GenerationBatchItemStatus.Adopted)
            {
                if (item.Attempts.Count == 0)
                    throw Invalid("batch.report_adopted_without_attempt", "An adopted item requires an attempt.");
                adoptedDurationMilliseconds = checked(
                    adoptedDurationMilliseconds + item.Attempts[^1].ActualDurationMilliseconds);
            }
        }

        int reviewedCount = checked(adoptedCount + rejectedCount);
        decimal? adoptionRate = reviewedCount == 0
            ? null
            : (decimal)adoptedCount / reviewedCount;
        decimal averageRetryCount = attemptedItemCount == 0
            ? 0m
            : (decimal)retryCount / attemptedItemCount;
        decimal? unitCost = costComplete && adoptedDurationMilliseconds > 0
            ? (decimal)actualCostMicros * 1_000m / adoptedDurationMilliseconds
            : null;

        return new GenerationBatchReport(
            batch.Items.Count,
            attemptedItemCount,
            attemptCount,
            retryCount,
            reviewedCount,
            adoptedCount,
            rejectedCount,
            adoptionRate,
            averageRetryCount,
            actualCostMicros,
            adoptedDurationMilliseconds,
            unitCost,
            costComplete,
            batch.Currency);
    }

    private static GenerationBatchValidationException Invalid(string code, string message) =>
        new(code, message);
}
