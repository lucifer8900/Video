using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Lingmai.RedMist.MediaPipeline;

namespace Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

internal sealed class RecordingObjectStorage : IObjectStorage
{
    private readonly Exception? _failure;

    public RecordingObjectStorage(Exception? failure = null) => _failure = failure;

    public int UploadCount { get; private set; }

    public ObjectStorageUpload? LastUpload { get; private set; }

    public int LargestReadRequest { get; private set; }

    public byte[] UploadedBytes { get; private set; } = [];

    public async Task<ObjectStorageWriteResult> UploadAsync(
        ObjectStorageUpload upload,
        CancellationToken cancellationToken)
    {
        UploadCount++;
        LastUpload = upload;
        if (_failure is not null) throw _failure;

        using var destination = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            int read = await upload.Content.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        UploadedBytes = destination.ToArray();
        return new ObjectStorageWriteResult(
            upload.ObjectKey,
            upload.ContentHash,
            Reused: false);
    }
}

internal sealed class RecordingMediaUrlSigner : IMediaUrlSigner
{
    private static readonly DateTimeOffset DefaultIssuedAtUtc =
        new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
    private readonly Exception? _failure;
    private readonly Uri _url;
    private readonly DateTimeOffset _issuedAtUtc;

    public RecordingMediaUrlSigner(
        Exception? failure = null,
        DateTimeOffset? issuedAtUtc = null)
    {
        _failure = failure;
        _issuedAtUtc = issuedAtUtc?.ToUniversalTime() ?? DefaultIssuedAtUtc;
        _url = new Uri(
            "https://storage.googleapis.com/redmist-test/media/item.mp4" +
            "?X-Goog-Algorithm=GOOG4-RSA-SHA256&X-Goog-Signature=unit-test-secret");
    }

    public int CallCount { get; private set; }

    public string? LastObjectKey { get; private set; }

    public TimeSpan LastTtl { get; private set; }

    public Task<SignedMediaUrl> CreateReadUrlAsync(
        string objectKey,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastObjectKey = objectKey;
        LastTtl = ttl;
        if (_failure is not null) throw _failure;
        return Task.FromResult(
            new SignedMediaUrl(_url, _issuedAtUtc.Add(ttl)));
    }
}

internal sealed class StaticGcsAccessTokenSource(string accessToken) : IGcsAccessTokenSource
{
    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(accessToken);
    }
}

internal sealed class DeterministicV4SignatureProvider(string signature)
    : IV4UrlSignatureProvider
{
    public string? LastStringToSign { get; private set; }

    public ValueTask<string> SignHexAsync(
        string stringToSign,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastStringToSign = stringToSign;
        return ValueTask.FromResult(signature);
    }
}

internal sealed class RecordingGcsHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public RecordingGcsHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string responseBody = "{\"name\":\"media/object.mp4\",\"generation\":\"1\"}")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    public int RequestCount { get; private set; }

    public Uri? RequestUri { get; private set; }

    public HttpMethod? Method { get; private set; }

    public AuthenticationHeaderValue? Authorization { get; private set; }

    public string? ContentType { get; private set; }

    public byte[] UploadedBytes { get; private set; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        RequestUri = request.RequestUri;
        Method = request.Method;
        Authorization = request.Headers.Authorization;
        ContentType = request.Content?.Headers.ContentType?.MediaType;
        if (request.Content is not null)
        {
            await using Stream source = await request.Content.ReadAsStreamAsync(cancellationToken);
            using var destination = new MemoryStream();
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            UploadedBytes = destination.ToArray();
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody),
        };
    }
}

internal sealed class GuardedReadStream : Stream
{
    private readonly MemoryStream _inner;
    private readonly int _maximumReadRequest;

    public GuardedReadStream(byte[] bytes, int maximumReadRequest)
    {
        _inner = new MemoryStream(bytes, writable: false);
        _maximumReadRequest = maximumReadRequest;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureBounded(count);
        return _inner.Read(buffer, offset, count);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureBounded(buffer.Length);
        return _inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    private void EnsureBounded(int count)
    {
        if (count > _maximumReadRequest)
        {
            throw new InvalidOperationException(
                $"Upload attempted a {count}-byte read; maximum is {_maximumReadRequest}.");
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class DistributionTempFile : IDisposable
{
    private DistributionTempFile(string path, byte[] bytes)
    {
        Path = path;
        File.WriteAllBytes(path, bytes);
    }

    public string Path { get; }

    public static DistributionTempFile Create(byte[] bytes)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"cx304-{Guid.NewGuid():N}.mp4");
        return new DistributionTempFile(path, bytes);
    }

    public void Dispose()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}

internal static class DistributionTestHash
{
    public static string Sha256(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
