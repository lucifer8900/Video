using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Generation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationBatchRequest(
    string SchemaVersion,
    string IdempotencyKey,
    string SourceManifestId,
    string SourceManifestContentHash,
    string TargetTier,
    IReadOnlyList<string> ShotIds,
    Guid? SourcePreviewBatchRef);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationBatchArtifactPin(
    string MediaRef,
    string AssetVersion,
    string ContentHash,
    string MediaType);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationBatchResultItem(
    string ShotId,
    string Status,
    Guid? GenerationJobRef,
    int AttemptCount,
    GenerationBatchArtifactPin? Artifact,
    int? ActualDurationMilliseconds,
    string ReviewStatus,
    string? FailureCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationBatchMetrics(
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
    bool CostComplete);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationBatchResult(
    string SchemaVersion,
    string RequestId,
    Guid BatchId,
    string SourceManifestId,
    string SourceManifestContentHash,
    string TargetTier,
    Guid? SourcePreviewBatchRef,
    string Status,
    string? ReasonCode,
    string Currency,
    IReadOnlyList<GenerationBatchResultItem> Items,
    GenerationBatchMetrics Metrics,
    string ContentHash);
