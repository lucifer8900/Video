using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Api.Intent;
using Lingmai.RedMist.Contracts.Intent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lingmai.RedMist.Api.Tests;

public sealed class IntentEndpointTests
{
    [Fact]
    public void PublicRequestContractContainsOnlyNodeIdAndTranscript()
    {
        var request = new IntentClassificationRequest("node.test", "测试表达");
        string json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(
            ["nodeId", "transcript"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task EndpointResolvesAllowListServerSideAndResponseHasNoStateOrEffects()
    {
        var classifier = new StubClassifier(new IntentDecision(
            IntentOutcome.Matched,
            "intent.allowed",
            0.9d,
            "mock"));
        var service = new IntentClassificationService(classifier, classifier);
        var catalog = new StubCatalog(
            "node.test",
            [new IntentCandidate("intent.allowed", ["同意"], 0.7d)]);
        DefaultHttpContext context = Context("{\"nodeId\":\"node.test\",\"transcript\":\"我同意\"}");

        IResult result = await IntentEndpoints.HandleAsync(context, catalog, service, CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("intent.allowed", response.RootElement.GetProperty("intentId").GetString());
        string[] names = response.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        Assert.Equal(
            ["confidence", "intentId", "outcome", "requestId", "schemaVersion", "source"],
            names);
        Assert.DoesNotContain("state", names);
        Assert.DoesNotContain("effects", names);
        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task EndpointRejectsClientSuppliedAllowListsOrEffects()
    {
        var classifier = new StubClassifier(new IntentDecision(IntentOutcome.Irrelevant, null, 0d, "mock"));
        var service = new IntentClassificationService(classifier, classifier);
        var catalog = new StubCatalog("node.test", []);
        DefaultHttpContext context = Context(
            "{\"nodeId\":\"node.test\",\"transcript\":\"任意表达\",\"allowedIntentIds\":[\"intent.injected\"],\"effects\":[]}");

        IResult result = await IntentEndpoints.HandleAsync(context, catalog, service, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, classifier.CallCount);
    }

    private static DefaultHttpContext Context(string json)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.TraceIdentifier = "intent-request-test";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument Response(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }

    private sealed class StubCatalog : IIntentCatalog
    {
        private readonly string _nodeId;
        private readonly IReadOnlyList<IntentCandidate> _allowed;

        public StubCatalog(string nodeId, IReadOnlyList<IntentCandidate> allowed)
        {
            _nodeId = nodeId;
            _allowed = allowed;
        }

        public int CallCount { get; private set; }

        public bool TryGetAllowedIntents(string nodeId, out IReadOnlyList<IntentCandidate> allowedIntents)
        {
            CallCount++;
            allowedIntents = _allowed;
            return string.Equals(nodeId, _nodeId, StringComparison.Ordinal);
        }
    }

    private sealed class StubClassifier : IIntentClassifier
    {
        private readonly IntentDecision _result;

        public StubClassifier(IntentDecision result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<IntentDecision> ClassifyAsync(
            IntentClassificationContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
