namespace Lingmai.RedMist.Generation.Batches;

public interface IGenerationBatchRepository
{
    Task<GenerationBatchCreateResult> CreateOrGetAsync(
        GenerationBatchSubmission submission,
        Guid batchId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<GenerationBatch?> GetAsync(Guid batchId, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task<GenerationBatchRunLease?> TryAcquireRunAsync(
        Guid batchId,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task ReleaseRunAsync(
        Guid batchId,
        string owner,
        long fencingToken,
        CancellationToken cancellationToken);

    Task<GenerationBatch> AppendAttemptAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        GenerationBatchAttempt attempt,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<GenerationBatch> FailItemAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        string failureCode,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<GenerationBatchReviewApplyResult> ApplyReviewsAsync(
        Guid batchId,
        long expectedVersion,
        string idempotencyKey,
        string reviewFingerprint,
        IReadOnlyList<GenerationBatchItemReview> reviews,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
