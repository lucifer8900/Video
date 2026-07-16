namespace Lingmai.RedMist.MediaPipeline;

public class MediaPipelineException : Exception
{
    public MediaPipelineException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed class MediaObjectConflictException : MediaPipelineException
{
    public MediaObjectConflictException()
        : base(
            "media.object_hash_conflict",
            "The immutable media object already exists with different content.")
    {
    }
}

public sealed record ObjectStorageUpload(
    string ObjectKey,
    string ContentHash,
    string ContentType,
    long ContentLength,
    Stream Content);

public sealed record ObjectStorageWriteResult(
    string ObjectKey,
    string ContentHash,
    bool Reused);

public interface IObjectStorage
{
    Task<ObjectStorageWriteResult> UploadAsync(
        ObjectStorageUpload upload,
        CancellationToken cancellationToken);
}

public interface IGcsAccessTokenSource
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed class GcsObjectStorageOptions
{
    public string BucketName { get; set; } = string.Empty;

    public long MaxObjectBytes { get; set; }
}

public sealed record SignedMediaUrl(Uri Url, DateTimeOffset ExpiresAtUtc)
{
    public override string ToString() =>
        $"SignedMediaUrl(host={Url.Host}, expiresAtUtc={ExpiresAtUtc:O})";
}

public interface IMediaUrlSigner
{
    Task<SignedMediaUrl> CreateReadUrlAsync(
        string objectKey,
        TimeSpan ttl,
        CancellationToken cancellationToken);
}

public interface IV4UrlSignatureProvider
{
    ValueTask<string> SignHexAsync(
        string stringToSign,
        CancellationToken cancellationToken);
}

public sealed class GcsV4SignedUrlOptions
{
    public string BucketName { get; set; } = string.Empty;

    public string ServiceAccountEmail { get; set; } = string.Empty;

    public TimeSpan MaxTtl { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed record MediaDistributionRequest(
    string TemporaryFilePath,
    string ContentType,
    string Extension);

public sealed record MediaDistributionResult(
    string ObjectKey,
    string ContentHash,
    SignedMediaUrl Download,
    bool Reused)
{
    public override string ToString() =>
        $"MediaDistributionResult(objectKey={ObjectKey}, contentHash={ContentHash}, " +
        $"expiresAtUtc={Download.ExpiresAtUtc:O}, reused={Reused})";
}

public sealed class MediaDistributionOptions
{
    public long MaxInputBytes { get; set; }

    public TimeSpan SignedUrlTtl { get; set; } = TimeSpan.FromMinutes(5);
}

internal static class MediaDistributionValidation
{
    public static void EnsureObjectKey(string objectKey)
    {
        bool invalid =
            string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.Length > 1024 ||
            objectKey.StartsWith("/", StringComparison.Ordinal) ||
            objectKey.EndsWith("/", StringComparison.Ordinal) ||
            objectKey.Contains("//", StringComparison.Ordinal) ||
            objectKey.Contains('\\') ||
            objectKey.Contains('?') ||
            objectKey.Contains('#') ||
            objectKey.Contains('%') ||
            objectKey.Any(char.IsControl) ||
            objectKey.Split('/').Any(segment => segment is "." or "..");
        if (invalid)
        {
            throw new MediaPipelineException(
                "media.object_key_invalid",
                "The media object key is invalid.");
        }
    }

    public static void EnsureContentHash(string contentHash)
    {
        bool valid =
            contentHash is { Length: 71 } &&
            contentHash.StartsWith("sha256:", StringComparison.Ordinal) &&
            contentHash.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;
        if (!valid)
        {
            throw new MediaPipelineException(
                "media.content_hash_invalid",
                "The media content hash is invalid.");
        }
    }

    public static void EnsureUpload(ObjectStorageUpload upload, long maxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(upload.Content);
        EnsureObjectKey(upload.ObjectKey);
        EnsureContentHash(upload.ContentHash);
        if (!upload.Content.CanRead)
        {
            throw new MediaPipelineException(
                "media.content_unreadable",
                "The media content stream is not readable.");
        }

        if (string.IsNullOrWhiteSpace(upload.ContentType) || upload.ContentType.Length > 128)
        {
            throw new MediaPipelineException(
                "media.content_type_invalid",
                "The media content type is invalid.");
        }

        if (upload.ContentLength < 0 || upload.ContentLength > maxObjectBytes)
        {
            throw new MediaPipelineException(
                "media.input_too_large",
                "The media input exceeds the size limit.");
        }
    }
}
