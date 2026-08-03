using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace Lingmai.RedMist.Api.Baseline;

public sealed class RequestLoggingMiddleware
{
    private static readonly EventId RequestCompleted = new(3010, nameof(RequestCompleted));

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        long started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            string safePath = context.GetEndpoint() is RouteEndpoint routeEndpoint
                ? routeEndpoint.RoutePattern.RawText ?? "matched"
                : "unmatched";
            _logger.LogInformation(
                RequestCompleted,
                "Request completed {RequestId} {Method} {Path} {StatusCode} {ElapsedMilliseconds}",
                context.TraceIdentifier,
                context.Request.Method,
                safePath,
                context.Response.StatusCode,
                elapsedMilliseconds);
        }
    }
}
