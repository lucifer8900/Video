using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class GcsObjectStorage : IObjectStorage
{
    private const string StorageHost = "storage.googleapis.com";
    private readonly HttpClient _httpClient;
    private readonly string _bucketName;
    private readonly long _maxObjectBytes;
    private readonly IGcsAccessTokenSource _accessTokenSource;

    public GcsObjectStorage(
        HttpClient httpClient,
        GcsObjectStorageOptions options,
        IGcsAccessTokenSource accessTokenSource)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _accessTokenSource = accessTokenSource ??
            throw new ArgumentNullException(nameof(accessTokenSource));
        if (!IsValidBucketName(options.BucketName))
            throw new ArgumentException("GCS bucket name is invalid.", nameof(options));
        if (options.MaxObjectBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        _bucketName = options.BucketName;
        _maxObjectBytes = options.MaxObjectBytes;
    }

    public async Task<ObjectStorageWriteResult> UploadAsync(
        ObjectStorageUpload upload,
        CancellationToken cancellationToken)
    {
        MediaDistributionValidation.EnsureUpload(upload, _maxObjectBytes);
        string accessToken = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        Uri uploadUri = BuildUploadUri(upload.ObjectKey);
        using var validatingStream = new ValidatingUploadStream(
            upload.Content,
            upload.ContentLength,
            _maxObjectBytes,
            upload.ContentHash);
        using var body = new StreamContent(validatingStream, 64 * 1024);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(upload.ContentType);
        body.Headers.ContentLength = upload.ContentLength;
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri)
        {
            Content = body,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            validatingStream.EnsureComplete();
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
            throw Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return await ResolveExistingAsync(
                        upload,
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode) throw Unavailable();
            return new ObjectStorageWriteResult(
                upload.ObjectKey,
                upload.ContentHash,
                Reused: false);
        }
    }

    private async Task<ObjectStorageWriteResult> ResolveExistingAsync(
        ObjectStorageUpload upload,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string escapedBucket = Uri.EscapeDataString(_bucketName);
        string escapedObject = Uri.EscapeDataString(upload.ObjectKey);
        var metadataUri = new Uri(
            $"https://{StorageHost}/storage/v1/b/{escapedBucket}/o/{escapedObject}" +
            "?fields=name%2Cmetadata");
        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Unavailable();
            try
            {
                await using Stream stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using JsonDocument json = await JsonDocument
                    .ParseAsync(
                        stream,
                        new JsonDocumentOptions { MaxDepth = 8 },
                        cancellationToken)
                    .ConfigureAwait(false);
                string? existingHash = json.RootElement
                    .GetProperty("metadata")
                    .GetProperty("contentSha256")
                    .GetString();
                if (!string.Equals(existingHash, upload.ContentHash, StringComparison.Ordinal))
                    throw new MediaObjectConflictException();

                return new ObjectStorageWriteResult(
                    upload.ObjectKey,
                    upload.ContentHash,
                    Reused: true);
            }
            catch (MediaObjectConflictException)
            {
                throw;
            }
            catch
            {
                throw Unavailable();
            }
        }
    }

    private async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            string token = await _accessTokenSource
                .GetAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl)) throw Unavailable();
            return token;
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
            throw Unavailable();
        }
    }

    private Uri BuildUploadUri(string objectKey)
    {
        string bucket = Uri.EscapeDataString(_bucketName);
        string name = Uri.EscapeDataString(objectKey);
        return new Uri(
            $"https://{StorageHost}/upload/storage/v1/b/{bucket}/o" +
            $"?uploadType=media&ifGenerationMatch=0&name={name}");
    }

    private static bool IsValidBucketName(string bucketName) =>
        bucketName is { Length: >= 3 and <= 222 } &&
        bucketName[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        bucketName[^1] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        bucketName.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');

    private static MediaPipelineException Unavailable() =>
        new("media.storage_unavailable", "Media object storage is unavailable.");

    private sealed class ValidatingUploadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _declaredLength;
        private readonly long _maximumLength;
        private readonly string _expectedHash;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _total;
        private bool _complete;

        public ValidatingUploadStream(
            Stream inner,
            long declaredLength,
            long maximumLength,
            string expectedHash)
        {
            _inner = inner;
            _declaredLength = declaredLength;
            _maximumLength = maximumLength;
            _expectedHash = expectedHash;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _declaredLength;
        public override long Position
        {
            get => _total;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            Observe(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Observe(buffer.Span[..read]);
            return read;
        }

        public void EnsureComplete()
        {
            if (!_complete)
            {
                throw new MediaPipelineException(
                    "media.content_length_mismatch",
                    "The media content was not fully uploaded.");
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }

        private void Observe(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                Complete();
                return;
            }

            _total = checked(_total + bytes.Length);
            if (_total > _maximumLength || _total > _declaredLength)
            {
                throw new MediaPipelineException(
                    "media.input_too_large",
                    "The media input exceeds the size limit.");
            }

            _hash.AppendData(bytes);
        }

        private void Complete()
        {
            if (_complete) return;
            if (_total != _declaredLength)
            {
                throw new MediaPipelineException(
                    "media.content_length_mismatch",
                    "The media content length did not match its declaration.");
            }

            string actualHash =
                "sha256:" + Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualHash, _expectedHash, StringComparison.Ordinal))
            {
                throw new MediaPipelineException(
                    "media.content_hash_mismatch",
                    "The media content hash did not match its declaration.");
            }

            _complete = true;
        }
    }
}
