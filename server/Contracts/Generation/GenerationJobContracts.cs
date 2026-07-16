using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Generation;

public static class GenerationJobStatuses
{
    public const string Created = "created";
    public const string Queued = "queued";
    public const string Generating = "generating";
    public const string Moderating = "moderating";
    public const string Transcoding = "transcoding";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Expired = "expired";

    public static readonly string[] All =
    [
        Created,
        Queued,
        Generating,
        Moderating,
        Transcoding,
        Ready,
        Failed,
        Expired,
    ];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationJobSubmitRequest(
    string SchemaVersion,
    string IdempotencyKey,
    string InputHash);

public sealed record GenerationJobStatusResponse(
    string SchemaVersion,
    string RequestId,
    Guid JobId,
    string Status,
    bool Terminal,
    int PollAfterMilliseconds,
    string? FailureCode);
