using Microsoft.Extensions.Primitives;

namespace Lingmai.RedMist.Api.Baseline;

public sealed class RequestIdMiddleware
{
    public const string HeaderName = "X-Request-ID";

    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string requestId = TryGetSafeIncomingId(context.Request.Headers, out string incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = requestId;
        await _next(context).ConfigureAwait(false);
    }

    private static bool TryGetSafeIncomingId(
        IHeaderDictionary headers,
        out string requestId)
    {
        if (!headers.TryGetValue(HeaderName, out StringValues values) || values.Count != 1)
        {
            requestId = string.Empty;
            return false;
        }

        string value = values[0] ?? string.Empty;
        if (value.Length is < 1 or > 64 || !value.All(IsSafeCharacter))
        {
            requestId = string.Empty;
            return false;
        }

        requestId = value;
        return true;
    }

    private static bool IsSafeCharacter(char value) =>
        value is >= 'a' and <= 'z' or
        >= 'A' and <= 'Z' or
        >= '0' and <= '9' or
        '.' or '_' or '-' or ':';
}
