namespace Lingmai.RedMist.Api.Baseline;

public sealed class ApiExceptionMiddleware
{
    private static readonly EventId RequestFailed = new(3011, nameof(RequestFailed));

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                RequestFailed,
                "Request cancelled {RequestId} {ExceptionType}",
                context.TraceIdentifier,
                nameof(OperationCanceledException));
        }
        catch (BadHttpRequestException exception)
        {
            int statusCode = exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;
            await HandleAsync(
                context,
                exception,
                statusCode,
                statusCode == StatusCodes.Status413PayloadTooLarge
                    ? "api.payload_too_large"
                    : "api.invalid_request",
                statusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The request payload is too large."
                    : "The request is invalid.").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleAsync(
                context,
                exception,
                StatusCodes.Status500InternalServerError,
                "api.internal_error",
                "An unexpected error occurred.").ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(
        HttpContext context,
        Exception exception,
        int statusCode,
        string code,
        string message)
    {
        _logger.LogError(
            RequestFailed,
            "Request failed {RequestId} {ExceptionType} {ErrorCode}",
            context.TraceIdentifier,
            exception.GetType().Name,
            code);
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        await ApiErrorWriter.WriteAsync(
            context,
            statusCode,
            code,
            message,
            context.RequestAborted).ConfigureAwait(false);
    }
}
