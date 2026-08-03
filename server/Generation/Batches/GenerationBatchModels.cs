namespace Lingmai.RedMist.Generation.Batches;

public enum GenerationBatchManifestApproval
{
    NeedsReview,
    Approved,
}

public enum GenerationBatchShotReadiness
{
    Blocked,
    NeedsReview,
    Ready,
}

public enum GenerationBatchTier
{
    PreviewFast,
    FinalQuality,
}

public enum GenerationBatchProviderMode
{
    Mock,
    External,
}

public enum GenerationBatchStatus
{
    Running,
    AwaitingReview,
    Reviewed,
    Failed,
}

public enum GenerationBatchItemStatus
{
    Pending,
    AwaitingReview,
    Adopted,
    Rejected,
    Failed,
}

public enum GenerationBatchReviewDecision
{
    Adopt,
    Reject,
    Retry,
}

public sealed record GenerationBatchManifest(
    string ManifestId,
    string ContentHash,
    GenerationBatchManifestApproval ApprovalStatus,
    bool ShotDispatchAllowed,
    string Currency,
    IReadOnlyList<GenerationBatchShot> Shots);

public sealed record GenerationBatchMediaPin(
    string MediaRef,
    string AssetVersion,
    string ContentHash,
    string MediaType);

public sealed record GenerationBatchFramePin(
    string MediaRef,
    string AssetVersion,
    string ContentHash,
    string MediaType,
    string Origin);

public sealed record GenerationBatchShot(
    string ShotId,
    string InputHash,
    bool DispatchAllowed,
    GenerationBatchShotReadiness ReadinessStatus,
    GenerationBatchTier RequestedTier,
    int TargetDurationMilliseconds,
    string AspectRatio,
    long MaximumCostMicros,
    string DialogueBinding,
    GenerationBatchFramePin? FirstFrame,
    GenerationBatchFramePin? LastFrame,
    GenerationBatchMediaPin? PrimaryMedia,
    GenerationBatchMediaPin FallbackMedia);

public sealed record StartGenerationBatchRequest(
    string IdempotencyKey,
    GenerationBatchManifest Manifest,
    IReadOnlyList<string> ShotIds);

public sealed record GenerationBatchItemReview(
    string ShotId,
    GenerationBatchReviewDecision Decision,
    int ExpectedAttemptNumber,
    string ExpectedArtifactHash);

public sealed record ReviewGenerationBatchRequest(
    string IdempotencyKey,
    Guid BatchId,
    IReadOnlyList<GenerationBatchItemReview> Reviews);

public sealed record PromoteGenerationBatchRequest(
    string IdempotencyKey,
    Guid SourcePreviewBatchId,
    IReadOnlyList<string> ShotIds);

public sealed record GenerationBatchArtifact(
    string ArtifactId,
    string MediaType,
    string ContentHash);

public sealed record GenerationBatchAttemptRequest(
    Guid BatchId,
    string ShotId,
    string InputHash,
    GenerationBatchTier Tier,
    int AttemptNumber,
    int TargetDurationMilliseconds,
    string AspectRatio,
    string DialogueBinding,
    GenerationBatchFramePin? FirstFrame,
    GenerationBatchFramePin? LastFrame,
    GenerationBatchMediaPin? PrimaryMedia,
    GenerationBatchMediaPin FallbackMedia);

public sealed record GenerationBatchAttemptReceipt(
    Guid GenerationJobId,
    GenerationBatchArtifact Artifact,
    int ActualDurationMilliseconds,
    long ActualCostMicros,
    bool CostSettled);

public sealed record GenerationBatchAttempt(
    int AttemptNumber,
    Guid GenerationJobId,
    GenerationBatchArtifact Artifact,
    int ActualDurationMilliseconds,
    long ActualCostMicros,
    bool CostSettled,
    DateTimeOffset CompletedAtUtc);

public sealed record GenerationBatchItem
{
    public required string ShotId { get; init; }

    public required string InputHash { get; init; }

    public required GenerationBatchTier RequestedTier { get; init; }

    public required GenerationBatchTier TargetTier { get; init; }

    public required int TargetDurationMilliseconds { get; init; }

    public required string AspectRatio { get; init; }

    public required long MaximumCostMicros { get; init; }

    public required string DialogueBinding { get; init; }

    public GenerationBatchFramePin? FirstFrame { get; init; }

    public GenerationBatchFramePin? LastFrame { get; init; }

    public GenerationBatchMediaPin? PrimaryMedia { get; init; }

    public required GenerationBatchMediaPin FallbackMedia { get; init; }

    public required GenerationBatchItemStatus Status { get; init; }

    public string? FailureCode { get; init; }

    public required IReadOnlyList<GenerationBatchAttempt> Attempts { get; init; }
}

public sealed record GenerationBatch
{
    public required Guid Id { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string RequestFingerprint { get; init; }

    public required string ManifestId { get; init; }

    public required string ManifestContentHash { get; init; }

    public required GenerationBatchTier TargetTier { get; init; }

    public Guid? SourcePreviewBatchId { get; init; }

    public required string Currency { get; init; }

    public required GenerationBatchStatus Status { get; init; }

    public required IReadOnlyList<GenerationBatchItem> Items { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public long Version { get; init; }
}

public sealed record GenerationBatchSubmission(
    string IdempotencyKey,
    string RequestFingerprint,
    string ManifestId,
    string ManifestContentHash,
    GenerationBatchTier TargetTier,
    Guid? SourcePreviewBatchId,
    string Currency,
    IReadOnlyList<GenerationBatchItem> Items);

public sealed record GenerationBatchCreateResult(
    GenerationBatch Batch,
    bool Created);

public sealed record GenerationBatchReviewApplyResult(
    GenerationBatch Batch,
    bool Applied);

public sealed record GenerationBatchRunLease(
    GenerationBatch Batch,
    string Owner,
    DateTimeOffset ExpiresAtUtc,
    long FencingToken);

public interface IGenerationBatchAttemptExecutor
{
    GenerationBatchProviderMode Mode { get; }

    Task<GenerationBatchAttemptReceipt> ExecuteAsync(
        GenerationBatchAttemptRequest request,
        CancellationToken cancellationToken);
}

public sealed class GenerationBatchValidationException : ArgumentException
{
    public GenerationBatchValidationException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}

public sealed class GenerationBatchGateException : InvalidOperationException
{
    public GenerationBatchGateException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}

public sealed class GenerationBatchIdempotencyConflictException : InvalidOperationException
{
    public GenerationBatchIdempotencyConflictException(string idempotencyKey)
        : base("The generation batch idempotency key was reused for another request.") =>
        IdempotencyKey = idempotencyKey;

    public string IdempotencyKey { get; }
}

public sealed class GenerationBatchConcurrencyException : InvalidOperationException
{
    public GenerationBatchConcurrencyException(Guid batchId, long expectedVersion, long actualVersion)
        : base($"Generation batch '{batchId}' has version {actualVersion}; expected {expectedVersion}.")
    {
        BatchId = batchId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public Guid BatchId { get; }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}
