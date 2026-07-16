using System.Net;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Api.Generation;
using Lingmai.RedMist.Contracts.Generation;
using Lingmai.RedMist.Generation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lingmai.RedMist.Api.Tests;

[Collection(ApiProcessCollection.Name)]
public sealed class GenerationJobEndpointTests
{
    private const string FirstHash =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondHash =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstSubmitAndReplayReturnTheSameQueuedJob()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        var request = new GenerationJobSubmitRequest(
            "1.0.0",
            "cx306-same-request",
            FirstHash);

        (DefaultHttpContext firstContext, JsonDocument first) = await Submit(request, service, "cx306-first");
        (DefaultHttpContext replayContext, JsonDocument replay) = await Submit(request, service, "cx306-replay");
        Guid replayJobId;
        using (first)
        using (replay)
        {
            Assert.Equal(StatusCodes.Status202Accepted, firstContext.Response.StatusCode);
            Assert.Equal(StatusCodes.Status202Accepted, replayContext.Response.StatusCode);
            Assert.Equal(
                first.RootElement.GetProperty("jobId").GetGuid(),
                replay.RootElement.GetProperty("jobId").GetGuid());
            Assert.Equal("queued", first.RootElement.GetProperty("status").GetString());
            Assert.False(first.RootElement.GetProperty("terminal").GetBoolean());
            Assert.Equal(1000, first.RootElement.GetProperty("pollAfterMilliseconds").GetInt32());
            Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("failureCode").ValueKind);
            replayJobId = replay.RootElement.GetProperty("jobId").GetGuid();
        }

        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
        GenerationJob? stored = await repository.GetAsync(
            replayJobId,
            CancellationToken.None);
        Assert.Equal(GenerationJobStatus.Queued, stored?.Status);
    }

    [Fact]
    public async Task SameIdempotencyKeyWithDifferentHashReturnsSanitizedConflict()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        await Submit(
            new GenerationJobSubmitRequest("1.0.0", "cx306-secret-key", FirstHash),
            service,
            "cx306-conflict-first");

        (DefaultHttpContext context, JsonDocument response) = await Submit(
            new GenerationJobSubmitRequest("1.0.0", "cx306-secret-key", SecondHash),
            service,
            "cx306-conflict-second");
        using (response)
        {
            string body = response.RootElement.GetRawText();
            Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
            Assert.Equal("generation.idempotency_conflict", response.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("cx306-secret-key", body, StringComparison.Ordinal);
            Assert.DoesNotContain(SecondHash, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DirectSubmitRejectsUnsafeIdempotencyKeyWithoutEcho()
    {
        var service = Service(new InMemoryGenerationJobRepository());
        const string unsafeKey = "cx306 unsafe key must not echo";

        (DefaultHttpContext context, JsonDocument response) = await Submit(
            new GenerationJobSubmitRequest("1.0.0", unsafeKey, FirstHash),
            service,
            "cx306-invalid-key");
        using (response)
        {
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.Equal("generation.invalid_request", response.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(unsafeKey, response.RootElement.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DisabledSubmissionGateDoesNotPersistAJob()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        DefaultHttpContext context = Context("cx306-disabled-unit");

        IResult result = await GenerationJobEndpoints.HandleSubmitAsync(
            context,
            new GenerationJobSubmitRequest("1.0.0", "cx306-disabled-unit", FirstHash),
            new GenerationSubmissionGate(enabled: false),
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("generation.disabled", response.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PollResponseIsStrictCamelCaseAndDoesNotLeakInternalFields()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        (DefaultHttpContext _, JsonDocument submitted) = await Submit(
            new GenerationJobSubmitRequest("1.0.0", "cx306-safe-surface", FirstHash),
            service,
            "cx306-submit-surface");
        Guid jobId = submitted.RootElement.GetProperty("jobId").GetGuid();
        submitted.Dispose();

        (DefaultHttpContext context, JsonDocument response) = await Poll(jobId, service, "cx306-poll");
        using (response)
        {
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal(
                [
                    "failureCode", "jobId", "pollAfterMilliseconds", "requestId",
                    "schemaVersion", "status", "terminal",
                ],
                response.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            string body = response.RootElement.GetRawText();
            Assert.DoesNotContain("idempotency", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("inputHash", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lease", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attempt", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(GenerationJobStatus.Failed, "failed", "generation.failed")]
    [InlineData(GenerationJobStatus.Expired, "expired", "generation.expired")]
    public async Task FailedAndExpiredPollsReturnStableSanitizedFailureCodes(
        GenerationJobStatus target,
        string expectedStatus,
        string expectedFailureCode)
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        GenerationJob created = await service.SubmitAsync(
            new GenerationJobSubmission("cx306-terminal-" + expectedStatus, FirstHash),
            CancellationToken.None);
        await repository.TransitionAsync(created.Id, created.Version, target, CancellationToken.None);

        (DefaultHttpContext context, JsonDocument response) = await Poll(
            created.Id,
            service,
            "cx306-" + expectedStatus);
        using (response)
        {
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal(expectedStatus, response.RootElement.GetProperty("status").GetString());
            Assert.True(response.RootElement.GetProperty("terminal").GetBoolean());
            Assert.Equal(0, response.RootElement.GetProperty("pollAfterMilliseconds").GetInt32());
            Assert.Equal(expectedFailureCode, response.RootElement.GetProperty("failureCode").GetString());
        }
    }

    [Fact]
    public async Task ModerationFailurePollReturnsTheSanitizedModerationCode()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = Service(repository);
        GenerationJob job = await service.SubmitAsync(
            new GenerationJobSubmission("cx306-moderation-rejected", FirstHash),
            CancellationToken.None);
        foreach (GenerationJobStatus status in new[]
                 {
                     GenerationJobStatus.Queued,
                     GenerationJobStatus.Generating,
                     GenerationJobStatus.Moderating,
                     GenerationJobStatus.Failed,
                 })
        {
            job = await repository.TransitionAsync(
                job.Id,
                job.Version,
                status,
                CancellationToken.None);
        }

        (DefaultHttpContext context, JsonDocument response) = await Poll(
            job.Id,
            service,
            "cx306-moderation-poll");
        using (response)
        {
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal("failed", response.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "moderation.rejected",
                response.RootElement.GetProperty("failureCode").GetString());
        }
    }

    [Fact]
    public async Task UnknownJobPollReturnsNotFound()
    {
        var service = Service(new InMemoryGenerationJobRepository());
        (DefaultHttpContext context, JsonDocument response) = await Poll(
            Guid.Parse("ca3b77df-dfcf-4392-84c9-e5d89dbe0483"),
            service,
            "cx306-not-found");
        using (response)
        {
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Equal("generation.not_found", response.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task DefaultRealHttpRouteRejectsGenerationInsteadOfCreatingADeadJob()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();
        const string body =
            "{\"schemaVersion\":\"1.0.0\",\"idempotencyKey\":\"cx306-disabled\"," +
            "\"inputHash\":\"" + FirstHash + "\"}";

        using HttpResponseMessage response = await server.Client.PostAsync(
            "/api/v1/generation/jobs",
            new StringContent(body, Encoding.UTF8, "application/json"));
        using JsonDocument payload = await ReadHttpResponse(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("generation.disabled", payload.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("cx306-disabled", payload.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(FirstHash, payload.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnablingGenerationFailsFastUntilBudgetWorkerAndDeliveryAreWired()
    {
        ApiProcessExitResult result = await ApiProcessHarness.RunToExitAsync(
            new Dictionary<string, string>
            {
                ["Generation__Enabled"] = "true",
            },
            TimeSpan.FromSeconds(5));

        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("GENERATION_RUNTIME_INCOMPLETE", result.Logs, StringComparison.Ordinal);
        Assert.DoesNotContain(FirstHash, result.Logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealHttpRouteRejectsUnmappedSubmitProperties()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();
        string body =
            "{\"schemaVersion\":\"1.0.0\",\"idempotencyKey\":\"cx306-unmapped\"," +
            "\"inputHash\":\"" + FirstHash + "\",\"providerApiKey\":\"forbidden\"}";

        using HttpResponseMessage response = await server.Client.PostAsync(
            "/api/v1/generation/jobs",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static GenerationJobService Service(InMemoryGenerationJobRepository repository) =>
        new(repository, new FixedTimeProvider(FixedNow));

    private static async Task<(DefaultHttpContext Context, JsonDocument Response)> Submit(
        GenerationJobSubmitRequest request,
        GenerationJobService service,
        string requestId)
    {
        DefaultHttpContext context = Context(requestId);
        IResult result = await GenerationJobEndpoints.HandleSubmitAsync(
            context,
            request,
            new GenerationSubmissionGate(enabled: true),
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        return (context, Response(context));
    }

    private static async Task<(DefaultHttpContext Context, JsonDocument Response)> Poll(
        Guid jobId,
        GenerationJobService service,
        string requestId)
    {
        DefaultHttpContext context = Context(requestId);
        IResult result = await GenerationJobEndpoints.HandleGetAsync(
            context,
            jobId,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        return (context, Response(context));
    }

    private static DefaultHttpContext Context(string requestId)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.TraceIdentifier = requestId;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument Response(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }

    private static async Task<JsonDocument> ReadHttpResponse(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonDocument.Parse(json);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
