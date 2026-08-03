using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class BundleVoiceInteractionCatalogTests
{
    [Fact]
    public void ApprovedNodeScopedRulesAndResponsesAreLoaded()
    {
        using var fixture = new CatalogFixture(ValidBundle());

        var catalog = new BundleVoiceInteractionCatalog(fixture.Options, new StubIntentCatalog());

        Assert.True(catalog.TryGetNodePolicy("node.test", out NodeVoicePolicy policy));
        Assert.Equal("response.safe", policy.InvalidInputResponses[InvalidInputKind.Silence]);
        Assert.True(policy.Responses.ContainsKey("response.safe"));
    }

    [Fact]
    public void UnapprovedResponseFailsClosedWithExactPointer()
    {
        using var fixture = new CatalogFixture(
            ValidBundle().Replace(
                "\"approvalStatus\": \"approved\", \"effects\"",
                "\"approvalStatus\": \"needs_review\", \"effects\"",
                StringComparison.Ordinal));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new BundleVoiceInteractionCatalog(fixture.Options, new StubIntentCatalog()));

        Assert.Contains("/npcResponses/0/approvalStatus", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleResponseOutsideCurrentNodeAllowlistFailsClosed()
    {
        using var fixture = new CatalogFixture(
            ValidBundle().Replace(
                "\"npcResponseRefs\": [\"response.safe\"]",
                "\"npcResponseRefs\": []",
                StringComparison.Ordinal).Replace(
                "\"voiceIntentRefs\": [\"intent.allowed\"]",
                "\"voiceIntentRefs\": []",
                StringComparison.Ordinal));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new BundleVoiceInteractionCatalog(fixture.Options, new StubIntentCatalog()));

        Assert.Contains("/sceneNodes/0/invalid_input_rules/abuse/npcResponseRef", error.Message, StringComparison.Ordinal);
    }

    private static string ValidBundle() =>
        """
        {
          "schemaVersion": "1.0.0",
          "approvalStatus": "approved",
          "voiceIntents": [
            {
              "schemaVersion": "1.0.0",
              "id": "intent.allowed",
              "npcResponseRef": "response.safe",
              "approvalStatus": "approved"
            }
          ],
          "npcResponses": [
            { "schemaVersion": "1.0.0", "id": "response.safe", "approvalStatus": "approved", "effects": [] }
          ],
          "sceneNodes": [
            {
              "id": "node.test",
              "voiceIntentRefs": ["intent.allowed"],
              "npcResponseRefs": ["response.safe"],
              "invalid_input_rules": {
                "abuse": { "npcResponseRef": "response.safe" },
                "irrelevant": { "npcResponseRef": "response.safe" },
                "too_long": { "npcResponseRef": "response.safe" },
                "silence": { "npcResponseRef": "response.safe" },
                "low_confidence": { "npcResponseRef": "response.safe" }
              }
            }
          ]
        }
        """;

    private sealed class StubIntentCatalog : IIntentCatalog
    {
        public bool TryGetAllowedIntents(string nodeId, out IReadOnlyList<IntentCandidate> allowedIntents)
        {
            allowedIntents = string.Equals(nodeId, "node.test", StringComparison.Ordinal)
                ? [new IntentCandidate("intent.allowed", ["同意"], 0.7d)]
                : [];
            return string.Equals(nodeId, "node.test", StringComparison.Ordinal);
        }
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly string _directory;

        public CatalogFixture(string json)
        {
            _directory = Path.Combine(Path.GetTempPath(), "cx204-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "story.bundle.json");
            File.WriteAllText(path, json);
            Options = new IntentOptions { StoryBundlePath = path };
        }

        public IntentOptions Options { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
