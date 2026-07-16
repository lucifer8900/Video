using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Api.Intent;
using Lingmai.RedMist.Contracts.Intent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lingmai.RedMist.Api.Tests;

public sealed class VoiceInteractionEndpointTests
{
    [Fact]
    public void PublicRequestContainsOnlyNodeIdAndTranscript()
    {
        string json = JsonSerializer.Serialize(
            new VoiceInteractionRequest("node.test", string.Empty),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(
            ["nodeId", "transcript"],
            document.RootElement.EnumerateObject().Select(value => value.Name).Order().ToArray());
    }

    [Fact]
    public async Task EmptyTranscriptBecomesServerDerivedSilenceReactionWithoutStateOrEffects()
    {
        var classifier = new VoiceInteractionServiceTests.CountingClassifier(new IntentDecision(
            IntentOutcome.Matched,
            "intent.allowed",
            1d,
            "must-not-run"));
        var service = new VoiceInteractionService(
            new InvalidInputDetector(),
            new IntentClassificationService(classifier, classifier),
            new NpcConsequenceGate());
        var catalog = new StubCatalog("node.test", VoiceInteractionServiceTests.Policy());
        DefaultHttpContext context = Context("{\"nodeId\":\"node.test\",\"transcript\":\"\"}");

        IResult result = await VoiceInteractionEndpoints.HandleAsync(
            context,
            catalog,
            service,
            new IntentOptions(),
            CancellationToken.None);
        await result.ExecuteAsync(context);
        using JsonDocument response = Response(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("silence", response.RootElement.GetProperty("inputKind").GetString());
        Assert.Equal("response.silence", response.RootElement.GetProperty("npcResponseId").GetString());
        Assert.Equal(0, classifier.CallCount);
        string[] names = response.RootElement.EnumerateObject().Select(value => value.Name).ToArray();
        Assert.DoesNotContain("effects", names);
        Assert.DoesNotContain("state", names);
        Assert.DoesNotContain("nextNodeId", names);
        Assert.DoesNotContain("transcript", names);
        Assert.DoesNotContain("text", names);
    }

    [Theory]
    [InlineData("allowedIntentIds", "[]")]
    [InlineData("effects", "[]")]
    [InlineData("confidence", "1")]
    [InlineData("inputKind", "\"silence\"")]
    [InlineData("npcResponseId", "\"response.injected\"")]
    [InlineData("confirmed", "true")]
    public async Task ClientCannotInjectServerDerivedInteractionFields(string property, string value)
    {
        var classifier = new VoiceInteractionServiceTests.CountingClassifier(
            new IntentDecision(IntentOutcome.Irrelevant, null, 0d, "fixture"));
        var service = new VoiceInteractionService(
            new InvalidInputDetector(),
            new IntentClassificationService(classifier, classifier),
            new NpcConsequenceGate());
        var catalog = new StubCatalog("node.test", VoiceInteractionServiceTests.Policy());
        DefaultHttpContext context = Context(
            "{\"nodeId\":\"node.test\",\"transcript\":\"test\",\"" + property + "\":" + value + "}");

        IResult result = await VoiceInteractionEndpoints.HandleAsync(
            context,
            catalog,
            service,
            new IntentOptions(),
            CancellationToken.None);
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
        context.TraceIdentifier = "voice-interaction-test";
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

    private sealed class StubCatalog : IVoiceInteractionCatalog
    {
        private readonly string _nodeId;
        private readonly NodeVoicePolicy _policy;

        public StubCatalog(string nodeId, NodeVoicePolicy policy)
        {
            _nodeId = nodeId;
            _policy = policy;
        }

        public int CallCount { get; private set; }

        public bool TryGetNodePolicy(string nodeId, out NodeVoicePolicy policy)
        {
            CallCount++;
            policy = _policy;
            return string.Equals(nodeId, _nodeId, StringComparison.Ordinal);
        }
    }
}
