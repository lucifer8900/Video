using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Media;

namespace Lingmai.RedMist.Api.Media;

public sealed class MediaDownloadGrant
{
    public MediaDownloadGrant(
        string mediaId,
        string contentHash,
        Uri downloadUrl,
        DateTimeOffset expiresAtUtc,
        string contentType,
        long length)
    {
        MediaId = !string.IsNullOrWhiteSpace(mediaId)
            ? mediaId
            : throw new ArgumentException("A media ID is required.", nameof(mediaId));
        ContentHash = IsSha256(contentHash)
            ? contentHash
            : throw new ArgumentException("A lowercase SHA-256 content hash is required.", nameof(contentHash));
        DownloadUrl = downloadUrl is { IsAbsoluteUri: true } &&
                      string.Equals(downloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                      string.IsNullOrEmpty(downloadUrl.UserInfo) &&
                      string.IsNullOrEmpty(downloadUrl.Fragment)
            ? downloadUrl
            : throw new ArgumentException(
                "An absolute HTTPS download URL without credentials or fragments is required.",
                nameof(downloadUrl));
        ExpiresAtUtc = expiresAtUtc;
        ContentType = IsContentType(contentType)
            ? contentType
            : throw new ArgumentException("A valid content type is required.", nameof(contentType));
        Length = length > 0 ? length : throw new ArgumentOutOfRangeException(nameof(length));
    }

    public string MediaId { get; }

    public string ContentHash { get; }

    public Uri DownloadUrl { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public string ContentType { get; }

    public long Length { get; }

    public override string ToString() =>
        $"MediaDownloadGrant(MediaId={MediaId}, ExpiresAtUtc={ExpiresAtUtc:O})";

    private static bool IsSha256(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) return false;
        int separator = value.IndexOf('/');
        return separator > 0 && separator == value.LastIndexOf('/') && separator < value.Length - 1;
    }
}

public interface IMediaDeliveryService
{
    Task<MediaDownloadGrant> CreateDownloadTicketAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}

public sealed class MediaDeliveryException : InvalidOperationException
{
    public MediaDeliveryException(string code, string message)
        : base(message)
    {
        Code = !string.IsNullOrWhiteSpace(code)
            ? code
            : throw new ArgumentException("A media error code is required.", nameof(code));
    }

    public string Code { get; }
}

public sealed class UnavailableMediaDeliveryService : IMediaDeliveryService
{
    public Task<MediaDownloadGrant> CreateDownloadTicketAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new MediaDeliveryException("media.not_ready", "Media is not ready.");
    }
}

public static class MediaDownloadEndpoints
{
    public static IEndpointRouteBuilder MapMediaDownloadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(
            "/api/v1/generation/jobs/{jobId:guid}/download-ticket",
            HandleAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        Guid jobId,
        IMediaDeliveryService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(service);
        try
        {
            MediaDownloadGrant grant = await service.CreateDownloadTicketAsync(
                jobId,
                cancellationToken).ConfigureAwait(false);
            if (grant.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new MediaDeliveryException(
                    "media.ticket_expired",
                    "The media download ticket has expired.");
            string requestId = string.IsNullOrWhiteSpace(context.TraceIdentifier)
                ? Guid.NewGuid().ToString("N")
                : context.TraceIdentifier;
            var response = new MediaDownloadTicketResponse(
                "1.0.0",
                requestId,
                grant.MediaId,
                grant.ContentHash,
                grant.DownloadUrl,
                grant.ExpiresAtUtc,
                grant.ContentType,
                grant.Length);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(response, contentType: "application/json");
        }
        catch (MediaDeliveryException error)
        {
            (int statusCode, string code, string message) = error.Code switch
            {
                "media.not_found" => (
                    StatusCodes.Status404NotFound,
                    "media.not_found",
                    "Media was not found."),
                "media.not_ready" => (
                    StatusCodes.Status409Conflict,
                    "media.not_ready",
                    "Media is not ready."),
                "media.ticket_expired" => (
                    StatusCodes.Status503ServiceUnavailable,
                    "media.ticket_expired",
                    "The media download ticket has expired."),
                _ => (
                    StatusCodes.Status503ServiceUnavailable,
                    "media.unavailable",
                    "Media is temporarily unavailable."),
            };
            return ApiErrorWriter.Result(context, statusCode, code, message);
        }
    }
}
