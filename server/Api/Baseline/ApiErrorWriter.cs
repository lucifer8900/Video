using Lingmai.RedMist.Contracts.Api;

namespace Lingmai.RedMist.Api.Baseline;

public static class ApiErrorWriter
{
    public static IResult Result(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Results.Json(
            Create(context, code, message),
            statusCode: statusCode);
    }

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(
            Create(context, code, message),
            cancellationToken).ConfigureAwait(false);
    }

    private static ApiErrorResponse Create(
        HttpContext context,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(context.TraceIdentifier))
            context.TraceIdentifier = Guid.NewGuid().ToString("N");
        return new ApiErrorResponse(
            "1.0.0",
            context.TraceIdentifier,
            code,
            message);
    }
}
