using System.Net;
using System.Net.Http.Headers;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderArtifactDownloaderTests
{
    private const string AllowedHost = "provider-artifacts.example.test";

    [Theory]
    [InlineData("http://provider-artifacts.example.test/output.mp4")]
    [InlineData("https://provider-artifacts.example.test.evil.invalid/output.mp4")]
    [InlineData("https://127.0.0.1/output.mp4")]
    [InlineData("https://user:password@provider-artifacts.example.test/output.mp4")]
    public async Task RejectsNonHttpsAndNonExactHostsBeforeSending(string source)
    {
        var handler = new RecordingHandler(_ => SuccessWith([1, 2, 3]));
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: 32);
        using var destination = new MemoryStream();

        ProviderArtifactDownloadException error = await Assert.ThrowsAsync<ProviderArtifactDownloadException>(
            () => downloader.DownloadAsync(new Uri(source), destination, CancellationToken.None));

        Assert.Equal("provider.artifact_url_rejected", error.Code);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task StreamsIntoCallerOwnedDestinationWithoutProviderAuthorization()
    {
        byte[] expected = Enumerable.Range(0, 96 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamingOnlyContent(expected),
        });
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: expected.Length);
        using var destination = new MemoryStream();

        await downloader.DownloadAsync(
            new Uri($"https://{AllowedHost}/output.mp4"),
            destination,
            CancellationToken.None);

        Assert.Equal(expected, destination.ToArray());
        Assert.Null(handler.LastAuthorization);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(typeof(Task), typeof(ProviderArtifactDownloader)
            .GetMethod(
                nameof(ProviderArtifactDownloader.DownloadAsync),
                [typeof(Uri), typeof(Stream), typeof(CancellationToken)])!
            .ReturnType);
    }

    [Fact]
    public async Task RedirectResponseIsRejectedAndLocationIsNeverFollowed()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://127.0.0.1/internal") },
        });
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: 32);
        using var destination = new MemoryStream();

        ProviderArtifactDownloadException error = await Assert.ThrowsAsync<ProviderArtifactDownloadException>(
            () => downloader.DownloadAsync(
                new Uri($"https://{AllowedHost}/redirect"),
                destination,
                CancellationToken.None));

        Assert.Equal("provider.artifact_redirect_rejected", error.Code);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task DeclaredOversizedArtifactIsRejectedBeforeBodyCopy()
    {
        const int limit = 16;
        var handler = new RecordingHandler(_ => SuccessWith(new byte[limit + 1]));
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: limit);
        using var destination = new MemoryStream();

        ProviderArtifactDownloadException error = await Assert.ThrowsAsync<ProviderArtifactDownloadException>(
            () => downloader.DownloadAsync(
                new Uri($"https://{AllowedHost}/too-large.mp4"),
                destination,
                CancellationToken.None));

        Assert.Equal("provider.artifact_too_large", error.Code);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task ChunkedArtifactCannotExceedLimit()
    {
        const int limit = 64;
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamingOnlyContent(new byte[limit + 1]),
        });
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: limit);
        using var destination = new MemoryStream();

        ProviderArtifactDownloadException error = await Assert.ThrowsAsync<ProviderArtifactDownloadException>(
            () => downloader.DownloadAsync(
                new Uri($"https://{AllowedHost}/chunked-too-large.mp4"),
                destination,
                CancellationToken.None));

        Assert.Equal("provider.artifact_too_large", error.Code);
        Assert.InRange(destination.Length, 0, limit);
    }

    [Fact]
    public async Task ProviderErrorDoesNotLeakSignedQueryOrResponseBody()
    {
        const string signature = "super-secret-query-signature";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($"vendor failure echoed X-Goog-Signature={signature}"),
        });
        using var client = new HttpClient(handler);
        var downloader = CreateDownloader(client, maxArtifactBytes: 64);
        using var destination = new MemoryStream();
        var source = new Uri(
            $"https://{AllowedHost}/failed.mp4?X-Goog-Signature={signature}&token=also-secret");

        ProviderArtifactDownloadException error = await Assert.ThrowsAsync<ProviderArtifactDownloadException>(
            () => downloader.DownloadAsync(source, destination, CancellationToken.None));
        string rendered = error.ToString();

        Assert.Equal("provider.artifact_unavailable", error.Code);
        Assert.DoesNotContain(signature, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Goog-Signature", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vendor failure", rendered, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderArtifactDownloader CreateDownloader(
        HttpClient client,
        long maxArtifactBytes) =>
        new(
            client,
            new ProviderArtifactDownloadOptions
            {
                AllowedHosts = [AllowedHost],
                MaxArtifactBytes = maxArtifactBytes,
            });

    private static HttpResponseMessage SuccessWith(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StreamingOnlyContent(byte[] bytes) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            throw new InvalidOperationException(
                "The downloader attempted to buffer the complete provider artifact.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
