using System.Text.Json;

namespace Lingmai.RedMist.Generation.Providers;

public sealed record VideoGenerationRequest
{
    public required Guid JobId { get; init; }

    public required string Prompt { get; init; }

    public required string InputHash { get; init; }

    public long Seed { get; init; }

    public required string AspectRatio { get; init; }

    public int DurationSeconds { get; init; }

    public IReadOnlyList<VideoGenerationInputImage> InputImages { get; init; } =
        Array.Empty<VideoGenerationInputImage>();
}

public sealed record VideoGenerationInputImage(
    string ContentType,
    byte[] Bytes,
    string Sha256);

public sealed record ImageGenerationRequest
{
    public required Guid JobId { get; init; }

    public required string Prompt { get; init; }

    public required string InputHash { get; init; }

    public long Seed { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

public sealed record TextGenerationRequest
{
    public required Guid JobId { get; init; }

    public required string Instruction { get; init; }

    public JsonElement Input { get; init; }

    public required string InputHash { get; init; }

    public long Seed { get; init; }

    public JsonElement OutputSchema { get; init; }
}

public enum ProviderOperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public sealed record ProviderArtifact(
    string ArtifactId,
    string ContentType,
    long Length,
    string Sha256,
    Uri? DownloadUri = null);

public sealed record ProviderOperation(
    string OperationId,
    ProviderOperationStatus Status,
    ProviderArtifact? Artifact = null,
    string? FailureCode = null);

public sealed record StructuredTextResult(JsonElement Output, string OutputHash);

public interface IVideoGenerationProvider
{
    Task<ProviderOperation> StartAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken);

    Task<ProviderOperation> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken);

    Task CancelAsync(string operationId, CancellationToken cancellationToken);

    Task DownloadAsync(
        ProviderArtifact artifact,
        Stream destination,
        CancellationToken cancellationToken);
}

public interface IImageGenerationProvider
{
    Task<ProviderArtifact> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken);
}

public interface ITextGenerationProvider
{
    Task<StructuredTextResult> GenerateAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken);
}

public static class GenerationProviderErrorCodes
{
    public const string InvalidRequest = "provider.invalid_request";
    public const string AuthenticationFailed = "provider.authentication_failed";
    public const string OperationNotFound = "provider.operation_not_found";
    public const string ArtifactNotFound = "provider.artifact_not_found";
    public const string InvalidResponse = "provider.invalid_response";
    public const string SafetyRejected = "provider.safety_rejected";
    public const string Unavailable = "provider.unavailable";
    public const string Timeout = "provider.timeout";
    public const string UnsafeDownload = "provider.unsafe_download";
    public const string DownloadTooLarge = "provider.download_too_large";
}

public sealed class GenerationProviderException : InvalidOperationException
{
    public GenerationProviderException(string code, string message, bool isTransient = false)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A provider error code is required.", nameof(code));
        Code = code;
        IsTransient = isTransient;
    }

    public string Code { get; }

    public bool IsTransient { get; }
}
