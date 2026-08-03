using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LipSyncReviewer;

public static class LipSyncReviewWebHost
{
    private const string TokenHeader = "X-Lip-Sync-Review-Token";

    public static async Task<RunningLipSyncReviewHost> StartAsync(
        LipSyncReviewSession session,
        LipSyncReviewMediaLease mediaLease,
        LipSyncReviewReportWriter reportWriter,
        string reportPath,
        string reviewerSubject,
        string sessionToken,
        int port,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mediaLease);
        ArgumentNullException.ThrowIfNull(reportWriter);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (!string.Equals(
                session.MediaBindingHash,
                mediaLease.MediaBindingHash,
                StringComparison.Ordinal))
        {
            throw new LipSyncReviewInputException(
                "review.media_session_mismatch",
                "The review session and locked media lease do not have the same binding.");
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LipSyncReviewWebHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Encoder = JavaScriptEncoder.Default;
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        WebApplication app = builder.Build();
        var completion = new TaskCompletionSource<WrittenLipSyncReviewReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        app.Use(async (context, next) =>
        {
            if (!AllowedHost(context.Request.Host.Host) || !AllowedOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { code = "review.host_invalid" })
                    .ConfigureAwait(false);
                return;
            }
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; script-src 'self'; style-src 'self'; media-src 'self'; " +
                "connect-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; " +
                "form-action 'none'; object-src 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            await next().ConfigureAwait(false);
        });

        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        app.MapGet("/review.js", () => Asset("review.js", "text/javascript; charset=utf-8"));
        app.MapGet("/review.css", () => Asset("review.css", "text/css; charset=utf-8"));
        app.MapGet("/api/session", () => Results.Json(SessionView(session, sessionToken)));
        app.MapGet("/media/{responseId}/video", async (HttpContext context, string responseId) =>
            await ServeMediaAsync(
                    context,
                    session,
                    mediaLease,
                    responseId,
                    LipSyncPlaybackKind.OriginalVideo)
                .ConfigureAwait(false));
        app.MapGet("/media/{responseId}/audio", async (HttpContext context, string responseId) =>
            await ServeMediaAsync(
                    context,
                    session,
                    mediaLease,
                    responseId,
                    LipSyncPlaybackKind.ReferenceAudio)
                .ConfigureAwait(false));
        app.MapPost("/api/playback", (HttpRequest http, PlaybackRequest request) =>
            Execute(() =>
            {
                LipSyncPlaybackKind kind = request.Kind switch
                {
                    "original_video" => LipSyncPlaybackKind.OriginalVideo,
                    "reference_audio" => LipSyncPlaybackKind.ReferenceAudio,
                    _ => throw new LipSyncReviewGateException(
                        "review.playback_kind",
                        "The playback kind is invalid."),
                };
                return session.CompletePlayback(
                    request.ResponseId,
                    request.ExpectedRevision,
                    kind,
                    request.PlayedMilliseconds,
                    Token(http));
            }));
        app.MapPost("/api/decision", (HttpRequest http, DecisionRequest request) =>
            Execute(() =>
            {
                if (request.Checks is null ||
                    request.Checks.RawAudioUnmasked is null ||
                    request.Checks.PronunciationPassed is null ||
                    request.Checks.SubtitlePassed is null ||
                    request.Checks.LipSyncPassed is null)
                {
                    throw new LipSyncReviewGateException(
                        "review.request_invalid",
                        "Decision checks are required.");
                }
                LipSyncReviewDecision decision = request.RequestedDecision switch
                {
                    "pass" => LipSyncReviewDecision.Pass,
                    "redo" => LipSyncReviewDecision.Redo,
                    _ => throw new LipSyncReviewGateException(
                        "review.decision",
                        "The review decision is invalid."),
                };
                return session.RecordDecision(
                    new LipSyncReviewDecisionRequest(
                        request.ResponseId,
                        request.ExpectedRevision,
                        decision,
                        new LipSyncReviewChecks(
                            request.Checks.RawAudioUnmasked.Value,
                            request.Checks.PronunciationPassed.Value,
                            request.Checks.SubtitlePassed.Value,
                            request.Checks.LipSyncPassed.Value)),
                    Token(http));
            }));
        app.MapPost("/api/export", async (HttpRequest http, CancellationToken requestCancellation) =>
            await ExecuteAsync(async () =>
            {
                session.Authorize(Token(http));
                await mediaLease.VerifyAsync(requestCancellation).ConfigureAwait(false);
                WrittenLipSyncReviewReport written = reportWriter.Write(
                    session,
                    reportPath,
                    reviewerSubject);
                completion.TrySetResult(written);
                return written.Report;
            }).ConfigureAwait(false));

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            IServer server = app.Services.GetRequiredService<IServer>();
            string? address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
            if (address is null)
                throw new InvalidOperationException("The local lip-sync review address was not assigned.");
            var baseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
            if (!IPAddress.TryParse(baseAddress.Host, out IPAddress? ipAddress) ||
                !IPAddress.IsLoopback(ipAddress))
            {
                throw new InvalidOperationException("The lip-sync reviewer must bind only to loopback.");
            }
            return new RunningLipSyncReviewHost(app, baseAddress, completion.Task);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ServeMediaAsync(
        HttpContext context,
        LipSyncReviewSession session,
        LipSyncReviewMediaLease mediaLease,
        string responseId,
        LipSyncPlaybackKind kind)
    {
        if (!mediaLease.TryGetPlaybackMedia(responseId, kind, out LipSyncPlaybackMediaDescriptor media))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { code = "review.unknown_response" })
                .ConfigureAwait(false);
            return;
        }

        (long Start, long End, bool Partial)? range = RequestedRange(
            context.Request.Headers.Range,
            media.Length);
        if (range is null)
        {
            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            context.Response.Headers.ContentRange = $"bytes */{media.Length}";
            return;
        }

        (long start, long end, bool partial) = range.Value;
        long remaining = checked(end - start + 1);
        context.Response.StatusCode = partial
            ? StatusCodes.Status206PartialContent
            : StatusCodes.Status200OK;
        context.Response.ContentType = media.ContentType;
        context.Response.ContentLength = remaining;
        context.Response.Headers.AcceptRanges = "bytes";
        if (partial) context.Response.Headers.ContentRange = $"bytes {start}-{end}/{media.Length}";

        byte[] buffer = new byte[64 * 1024];
        long offset = start;
        while (remaining > 0)
        {
            int requested = checked((int)Math.Min(buffer.Length, remaining));
            int read = await mediaLease.ReadPlaybackAsync(
                    responseId,
                    kind,
                    offset,
                    buffer.AsMemory(0, requested),
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (read <= 0)
                throw new LipSyncReviewInputException(
                    "media.read_incomplete",
                    "Locked review media ended before the requested range was delivered.");
            await context.Response.Body.WriteAsync(
                    buffer.AsMemory(0, read),
                    context.RequestAborted)
                .ConfigureAwait(false);
            offset += read;
            remaining -= read;
        }
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
        session.RecordMediaDelivery(responseId, kind, start, end, media.Length);
    }

    private static (long Start, long End, bool Partial)? RequestedRange(
        string? header,
        long length)
    {
        if (length <= 0) return null;
        if (string.IsNullOrWhiteSpace(header)) return (0, length - 1, false);
        if (!RangeHeaderValue.TryParse(header, out RangeHeaderValue? parsed) ||
            !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            parsed.Ranges.Count != 1)
        {
            return null;
        }
        RangeItemHeaderValue item = parsed.Ranges.Single();
        long start;
        long end;
        if (item.From is null)
        {
            if (item.To is null or <= 0) return null;
            long suffixLength = Math.Min(item.To.Value, length);
            start = length - suffixLength;
            end = length - 1;
        }
        else
        {
            start = item.From.Value;
            end = Math.Min(item.To ?? length - 1, length - 1);
        }
        return start < 0 || start >= length || end < start ? null : (start, end, true);
    }

    private static bool AllowedHost(string host) =>
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

    private static bool AllowedOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
        {
            return true;
        }
        return Uri.TryCreate(values.ToString(), UriKind.Absolute, out Uri? origin) &&
               origin.Scheme == Uri.UriSchemeHttp &&
               AllowedHost(origin.Host) &&
               origin.Port == request.Host.Port;
    }

    private static object SessionView(LipSyncReviewSession session, string sessionToken)
    {
        Dictionary<string, LipSyncReviewItemState> states = session.Snapshot()
            .ToDictionary(state => state.ResponseId, StringComparer.Ordinal);
        return new
        {
            sessionToken,
            reviewScope = "pronunciation_subtitle_lip_sync",
            productionReadiness = "not_evaluated",
            authority = "human_review_export_not_promotion_authority",
            sourceManifestId = session.Manifest.ManifestId,
            sourceManifestContentHash = session.Manifest.ManifestContentHash,
            items = session.Manifest.ReviewableItems.Select(candidate =>
            {
                LipSyncReviewItemState state = states[candidate.ResponseId];
                return new
                {
                    candidate.ResponseId,
                    candidate.SourceNodeIds,
                    dialogueText = candidate.Dialogue.Text,
                    emotionText = candidate.Emotion.Text,
                    candidate.SourceIssueCodes,
                    state.Revision,
                    state.VideoPlaybackCompleted,
                    state.VideoPlayedMilliseconds,
                    state.AudioPlaybackCompleted,
                    state.AudioPlayedMilliseconds,
                    decision = state.Decision is null
                        ? null
                        : state.Decision == LipSyncReviewDecision.Pass ? "pass" : "redo",
                    state.Checks,
                    state.ReasonCodes,
                    videoUrl = $"/media/{Uri.EscapeDataString(candidate.ResponseId)}/video",
                    audioUrl = $"/media/{Uri.EscapeDataString(candidate.ResponseId)}/audio",
                };
            }),
            blockedItems = session.Manifest.BlockedItems,
        };
    }

    private static IResult Execute<T>(Func<T> action)
    {
        try
        {
            return Results.Json(action());
        }
        catch (LipSyncReviewGateException exception)
        {
            int status = exception.Code switch
            {
                "review.token_invalid" => StatusCodes.Status403Forbidden,
                "review.revision_stale" or "review.session_sealed" or "report.exists" =>
                    StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Json(new { code = exception.Code }, statusCode: status);
        }
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Json(await action().ConfigureAwait(false));
        }
        catch (LipSyncReviewGateException exception)
        {
            int status = exception.Code switch
            {
                "review.token_invalid" => StatusCodes.Status403Forbidden,
                "review.revision_stale" or "review.session_sealed" or "report.exists" =>
                    StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Json(new { code = exception.Code }, statusCode: status);
        }
        catch (LipSyncReviewInputException exception)
        {
            return Results.Json(new { code = exception.Code }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static string Token(HttpRequest request) =>
        request.Headers.TryGetValue(TokenHeader, out var values) ? values.ToString() : string.Empty;

    private static IResult Asset(string fileName, string contentType)
    {
        Assembly assembly = typeof(LipSyncReviewWebHost).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith("wwwroot." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Results.Text(reader.ReadToEnd(), contentType);
    }

    private sealed record PlaybackRequest(
        string ResponseId,
        long ExpectedRevision,
        string Kind,
        long PlayedMilliseconds);

    private sealed record DecisionRequest(
        string ResponseId,
        long ExpectedRevision,
        string RequestedDecision,
        ChecksRequest? Checks);

    private sealed record ChecksRequest(
        bool? RawAudioUnmasked,
        bool? PronunciationPassed,
        bool? SubtitlePassed,
        bool? LipSyncPassed);
}

public sealed class RunningLipSyncReviewHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private int _disposed;

    internal RunningLipSyncReviewHost(
        WebApplication application,
        Uri baseAddress,
        Task<WrittenLipSyncReviewReport> completion)
    {
        _application = application;
        BaseAddress = baseAddress;
        Completion = completion;
    }

    public Uri BaseAddress { get; }

    public Task<WrittenLipSyncReviewReport> Completion { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
