using System.Text.Json;
using Lingmai.RedMist.Api.Baseline;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingmai.RedMist.Api.Tests;

public sealed class ApiBaselineUnitTests
{
    private const string RequestIdHeader = "X-Request-ID";

    [Fact]
    public void OptionsValidatorRejectsBlankServiceName()
    {
        ApiBaselineOptions options = ValidOptions();
        options.ServiceName = "   ";

        Assert.Throws<InvalidOperationException>(() => ApiBaselineOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OptionsValidatorRejectsNonPositivePermitLimit(int permitLimit)
    {
        ApiBaselineOptions options = ValidOptions();
        options.RateLimit.PermitLimit = permitLimit;

        Assert.Throws<InvalidOperationException>(() => ApiBaselineOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void OptionsValidatorRejectsWindowOutsideOneThrough3600Seconds(int windowSeconds)
    {
        ApiBaselineOptions options = ValidOptions();
        options.RateLimit.WindowSeconds = windowSeconds;

        Assert.Throws<InvalidOperationException>(() => ApiBaselineOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void OptionsValidatorRejectsAnyNonZeroQueueLimit(int queueLimit)
    {
        ApiBaselineOptions options = ValidOptions();
        options.RateLimit.QueueLimit = queueLimit;

        Assert.Throws<InvalidOperationException>(() => ApiBaselineOptionsValidator.Validate(options));
    }

    [Fact]
    public void OptionsValidatorAcceptsValidOptions()
    {
        ApiBaselineOptionsValidator.Validate(ValidOptions());
    }

    [Fact]
    public async Task RequestIdMiddlewarePreservesAndEchoesValidRequestId()
    {
        const string requestId = "0123456789abcdef0123456789abcdef";
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestIdHeader] = requestId;
        string? observedRequestId = null;
        var middleware = new RequestIdMiddleware(nextContext =>
        {
            observedRequestId = nextContext.TraceIdentifier;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(requestId, observedRequestId);
        Assert.Equal(requestId, context.TraceIdentifier);
        Assert.Equal(requestId, context.Response.Headers[RequestIdHeader].ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid request id with spaces")]
    [InlineData("invalid/request/id")]
    [InlineData("中文请求标识")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task RequestIdMiddlewareReplacesMissingOrInvalidRequestId(string requestId)
    {
        var context = new DefaultHttpContext();
        if (requestId.Length > 0) context.Request.Headers[RequestIdHeader] = requestId;
        var middleware = new RequestIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.NotEqual(requestId, context.TraceIdentifier);
        Assert.True(Guid.TryParseExact(context.TraceIdentifier, "N", out _));
        Assert.Equal(context.TraceIdentifier, context.Response.Headers[RequestIdHeader].ToString());
    }

    [Fact]
    public async Task ExceptionMiddlewareReturnsUniformSanitizedInternalError()
    {
        const string requestId = "fedcba9876543210fedcba9876543210";
        const string secret = "CX301_EXCEPTION_SECRET_DO_NOT_LEAK";
        var context = new DefaultHttpContext();
        context.TraceIdentifier = requestId;
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(
            _ => Task.FromException(new InvalidOperationException(secret)),
            NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["code", "message", "requestId", "schemaVersion"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(requestId, root.GetProperty("requestId").GetString());
        Assert.Equal("api.internal_error", root.GetProperty("code").GetString());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("message").GetString());
    }

    private static ApiBaselineOptions ValidOptions()
    {
        var options = new ApiBaselineOptions
        {
            ServiceName = "lingmai-redmist-api",
        };
        options.RateLimit.PermitLimit = 60;
        options.RateLimit.WindowSeconds = 60;
        options.RateLimit.QueueLimit = 0;
        return options;
    }
}
