namespace Lingmai.RedMist.MediaPipeline;

public sealed class MediaDistributionPipeline
{
    private readonly IObjectStorage _objectStorage;
    private readonly IMediaUrlSigner _urlSigner;
    private readonly TimeProvider _timeProvider;
    private readonly long _maxInputBytes;
    private readonly TimeSpan _signedUrlTtl;
    private readonly MediaContentHasher _hasher = new();

    public MediaDistributionPipeline(
        IObjectStorage objectStorage,
        IMediaUrlSigner urlSigner,
        TimeProvider timeProvider,
        MediaDistributionOptions options)
    {
        _objectStorage = objectStorage ?? throw new ArgumentNullException(nameof(objectStorage));
        _urlSigner = urlSigner ?? throw new ArgumentNullException(nameof(urlSigner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxInputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.SignedUrlTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));

        _maxInputBytes = options.MaxInputBytes;
        _signedUrlTtl = options.SignedUrlTtl;
    }

    public async Task<MediaDistributionResult> DistributeAsync(
        MediaDistributionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? temporaryPath = null;
        try
        {
            temporaryPath = ValidateRequest(request);
            var file = new FileInfo(temporaryPath);
            if (!file.Exists)
            {
                throw new MediaPipelineException(
                    "media.input_missing",
                    "The temporary media input is unavailable.");
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new MediaPipelineException(
                    "media.input_invalid",
                    "The temporary media input is invalid.");
            }

            if (file.Length > _maxInputBytes)
            {
                throw new MediaPipelineException(
                    "media.input_too_large",
                    "The media input exceeds the size limit.");
            }

            string contentHash;
            await using (var hashingStream = OpenRead(temporaryPath))
            {
                contentHash = await _hasher
                    .ComputeSha256Async(hashingStream, cancellationToken)
                    .ConfigureAwait(false);
            }

            string hashHex = contentHash["sha256:".Length..];
            string objectKey =
                $"media/sha256/{hashHex[..2]}/{hashHex}{request.Extension}";
            MediaDistributionValidation.EnsureObjectKey(objectKey);

            ObjectStorageWriteResult stored;
            await using (var uploadStream = OpenRead(temporaryPath))
            {
                stored = await _objectStorage
                    .UploadAsync(
                        new ObjectStorageUpload(
                            objectKey,
                            contentHash,
                            request.ContentType,
                            file.Length,
                            uploadStream),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            SignedMediaUrl signed = await _urlSigner
                .CreateReadUrlAsync(stored.ObjectKey, _signedUrlTtl, cancellationToken)
                .ConfigureAwait(false);
            if (!signed.Url.IsAbsoluteUri ||
                !string.Equals(
                    signed.Url.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(signed.Url.UserInfo) ||
                signed.ExpiresAtUtc <= _timeProvider.GetUtcNow().ToUniversalTime())
            {
                throw new MediaPipelineException(
                    "media.signed_url_invalid",
                    "The signed media URL is invalid.");
            }

            return new MediaDistributionResult(
                stored.ObjectKey,
                stored.ContentHash,
                signed,
                stored.Reused);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MediaPipelineException)
        {
            throw;
        }
        catch
        {
            throw new MediaPipelineException(
                "media.distribution_failed",
                "Media distribution failed.");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Never replace the stable pipeline outcome with an OS path-bearing error.
                    // Cleanup failures are intentionally not exposed to callers or logs here.
                }
            }
        }
    }

    private static string ValidateRequest(MediaDistributionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TemporaryFilePath))
        {
            throw new MediaPipelineException(
                "media.input_invalid",
                "The temporary media input is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.ContentType) ||
            request.ContentType.Length > 128)
        {
            throw new MediaPipelineException(
                "media.content_type_invalid",
                "The media content type is invalid.");
        }

        bool validExtension =
            request.Extension is { Length: >= 2 and <= 9 } &&
            request.Extension[0] == '.' &&
            request.Extension.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789") < 0;
        if (!validExtension)
        {
            throw new MediaPipelineException(
                "media.extension_invalid",
                "The media file extension is invalid.");
        }

        try
        {
            return Path.GetFullPath(request.TemporaryFilePath);
        }
        catch
        {
            throw new MediaPipelineException(
                "media.input_invalid",
                "The temporary media input is invalid.");
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
}
