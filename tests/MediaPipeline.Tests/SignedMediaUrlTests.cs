using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class SignedMediaUrlTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatesShortLivedHttpsV4ReadUrlForExactStorageHost()
    {
        var signatureProvider = new DeterministicV4SignatureProvider(new string('a', 128));
        var signer = CreateSigner(signatureProvider);

        SignedMediaUrl signed = await signer.CreateReadUrlAsync(
            "media/sha256/ab/immutable-video.mp4",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Dictionary<string, string> query = ParseQuery(signed.Url);

        Assert.Equal("https", signed.Url.Scheme);
        Assert.Equal("storage.googleapis.com", signed.Url.Host);
        Assert.Equal(
            "/redmist-test/media/sha256/ab/immutable-video.mp4",
            signed.Url.AbsolutePath);
        Assert.Equal("GOOG4-RSA-SHA256", query["X-Goog-Algorithm"]);
        Assert.Equal("20260716T083000Z", query["X-Goog-Date"]);
        Assert.Equal("300", query["X-Goog-Expires"]);
        Assert.Equal("host", query["X-Goog-SignedHeaders"]);
        Assert.Equal(new string('a', 128), query["X-Goog-Signature"]);
        Assert.Contains("/20260716/auto/storage/goog4_request", query["X-Goog-Credential"], StringComparison.Ordinal);
        Assert.Equal(FixedNow.AddMinutes(5), signed.ExpiresAtUtc);
        Assert.NotNull(signatureProvider.LastStringToSign);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(901)]
    public async Task RejectsNonPositiveOrOverlongTtl(int seconds)
    {
        var signer = CreateSigner(new DeterministicV4SignatureProvider(new string('b', 128)));

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => signer.CreateReadUrlAsync(
                "media/sha256/ab/immutable-video.mp4",
                TimeSpan.FromSeconds(seconds),
                CancellationToken.None));

        Assert.Equal("media.signed_url_ttl_invalid", error.Code);
    }

    [Fact]
    public async Task SignedUrlToStringNeverLeaksQuerySignature()
    {
        string signature = new('c', 128);
        var signer = CreateSigner(new DeterministicV4SignatureProvider(signature));
        SignedMediaUrl signed = await signer.CreateReadUrlAsync(
            "media/sha256/ab/immutable-video.mp4",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        string rendered = signed.ToString();

        Assert.DoesNotContain(signature, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Goog-", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(signed.Url.Query, rendered, StringComparison.Ordinal);
    }

    private static GcsV4MediaUrlSigner CreateSigner(
        IV4UrlSignatureProvider signatureProvider) =>
        new(
            new GcsV4SignedUrlOptions
            {
                BucketName = "redmist-test",
                ServiceAccountEmail = "media-signer@redmist-test.iam.gserviceaccount.com",
                MaxTtl = TimeSpan.FromMinutes(15),
            },
            signatureProvider,
            new FixedTimeProvider(FixedNow));

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(component => component.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal);
}
