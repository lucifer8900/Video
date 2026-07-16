using System.Text.Json;
using Lingmai.RedMist.Api.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lingmai.RedMist.Api.Tests;

public sealed class MediaDownloadEndpointTests
{
    private static readonly Guid ReadyJobId =
        Guid.Parse("1e353539-8761-4daa-9879-67dd81e4bb31");

    [Fact]
    public async Task ReadyMediaReturnsOnlyAJsonDownloadTicket()
    {
        var service = new StubMediaDeliveryService(new MediaDownloadGrant(
            "media.test.ready",
            $"sha256:{new string('a', 64)}",
            new Uri("https://cdn.example.test/media.mp4?X-Goog-Signature=TEST_ONLY"),
            new DateTimeOffset(2026, 7, 16, 12, 5, 0, TimeSpan.Zero),
            "video/mp4",
            4096));
        DefaultHttpContext context = Context();

        IResult result = await MediaDownloadEndpoints.HandleAsync(
            context,
            ReadyJobId,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(
            [
                "contentHash", "contentType", "downloadUrl", "expiresAtUtc", "length",
                "mediaId", "requestId", "schemaVersion",
            ],
            response.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("media.test.ready", response.RootElement.GetProperty("mediaId").GetString());
        Assert.Equal("media-request-test", response.RootElement.GetProperty("requestId").GetString());
        string serialized = response.RootElement.GetRawText();
        Assert.DoesNotContain("bytes", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bucket", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task NotReadyMediaReturnsSmallUnifiedJsonError()
    {
        var service = new StubMediaDeliveryService(
            new MediaDeliveryException("media.not_ready", "Media is not ready."));
        DefaultHttpContext context = Context();

        IResult result = await MediaDownloadEndpoints.HandleAsync(
            context,
            ReadyJobId,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("media.not_ready", response.RootElement.GetProperty("code").GetString());
        Assert.True(context.Response.Body.Length < 2048);
    }

    [Fact]
    public async Task DeliveryFailureNeverEchoesServiceDetailsOrSignedCredentials()
    {
        const string secret = "X-Goog-Signature=must-never-reach-the-client";
        var service = new StubMediaDeliveryService(
            new MediaDeliveryException("media.not_ready", "upstream " + secret));
        DefaultHttpContext context = Context();

        IResult result = await MediaDownloadEndpoints.HandleAsync(
            context,
            ReadyJobId,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("media.not_ready", response.RootElement.GetProperty("code").GetString());
        Assert.Equal("Media is not ready.", response.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain(secret, response.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredDownloadTicketIsRejectedWithoutExposingTheSignedUrl()
    {
        var service = new StubMediaDeliveryService(new MediaDownloadGrant(
            "media.test.expired",
            $"sha256:{new string('b', 64)}",
            new Uri("https://cdn.example.test/expired.mp4?X-Goog-Signature=TEST_ONLY"),
            DateTimeOffset.UnixEpoch,
            "video/mp4",
            4096));
        DefaultHttpContext context = Context();

        IResult result = await MediaDownloadEndpoints.HandleAsync(
            context,
            ReadyJobId,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("media.ticket_expired", response.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("X-Goog-Signature", response.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadGrantRejectsSchemaInvalidOrCredentialBearingMetadata()
    {
        DateTimeOffset future = DateTimeOffset.UtcNow.AddMinutes(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaDownloadGrant(
            "media.test.zero",
            $"sha256:{new string('c', 64)}",
            new Uri("https://cdn.example.test/video.mp4"),
            future,
            "video/mp4",
            0));
        Assert.Throws<ArgumentException>(() => new MediaDownloadGrant(
            "media.test.hash",
            "sha256:not-a-digest",
            new Uri("https://cdn.example.test/video.mp4"),
            future,
            "video/mp4",
            1));
        Assert.Throws<ArgumentException>(() => new MediaDownloadGrant(
            "media.test.userinfo",
            $"sha256:{new string('d', 64)}",
            new Uri("https://user:secret@cdn.example.test/video.mp4"),
            future,
            "video/mp4",
            1));
        Assert.Throws<ArgumentException>(() => new MediaDownloadGrant(
            "media.test.fragment",
            $"sha256:{new string('e', 64)}",
            new Uri("https://cdn.example.test/video.mp4#fragment"),
            future,
            "video/mp4",
            1));
    }

    [Fact]
    public void PublicMediaEndpointSurfaceCannotReturnVideoBytesOrStreams()
    {
        Type endpointType = typeof(MediaDownloadEndpoints);
        Type[] forbidden = [typeof(byte[]), typeof(Stream), typeof(FileStream), typeof(IFormFile)];

        foreach (var method in endpointType.GetMethods(
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Static |
                     System.Reflection.BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain(method.ReturnType, forbidden);
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => forbidden.Contains(parameter.ParameterType));
        }
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.TraceIdentifier = "media-request-test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument Response(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }

    private sealed class StubMediaDeliveryService : IMediaDeliveryService
    {
        private readonly MediaDownloadGrant? _grant;
        private readonly Exception? _error;

        public StubMediaDeliveryService(MediaDownloadGrant grant) => _grant = grant;

        public StubMediaDeliveryService(Exception error) => _error = error;

        public int CallCount { get; private set; }

        public Task<MediaDownloadGrant> CreateDownloadTicketAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_error is not null) throw _error;
            return Task.FromResult(_grant!);
        }
    }
}
