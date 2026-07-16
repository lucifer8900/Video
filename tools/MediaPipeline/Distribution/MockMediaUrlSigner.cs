namespace Lingmai.RedMist.MediaPipeline;

public sealed class MockMediaUrlSigner : IMediaUrlSigner
{
    private readonly Uri _baseUri;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxTtl;

    public MockMediaUrlSigner(Uri baseUri, TimeProvider timeProvider, TimeSpan maxTtl)
    {
        _baseUri = baseUri is { IsAbsoluteUri: true } &&
                   string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrEmpty(baseUri.UserInfo) &&
                   string.IsNullOrEmpty(baseUri.Query)
            ? baseUri
            : throw new ArgumentException("An absolute HTTPS base URI without credentials or query is required.", nameof(baseUri));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _maxTtl = maxTtl > TimeSpan.Zero
            ? maxTtl
            : throw new ArgumentOutOfRangeException(nameof(maxTtl));
    }

    public Task<SignedMediaUrl> CreateReadUrlAsync(
        string objectKey,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaDistributionValidation.EnsureObjectKey(objectKey);
        if (ttl <= TimeSpan.Zero || ttl > _maxTtl)
        {
            throw new MediaPipelineException(
                "media.signed_url_ttl_invalid",
                "The signed media URL lifetime is invalid.");
        }

        string encodedPath = string.Join(
            '/',
            objectKey.Split('/').Select(Uri.EscapeDataString));
        Uri url = new(_baseUri, encodedPath);
        return Task.FromResult(new SignedMediaUrl(
            url,
            _timeProvider.GetUtcNow().ToUniversalTime().Add(ttl)));
    }
}
