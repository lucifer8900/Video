using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;

namespace Contracts.Schema.Tests;

public sealed class StoryThreadContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    [Fact]
    public void EffectSerializationOmitsMembersForbiddenByTheSelectedOperation()
    {
        var effect = new StoryThreadEffectContract(
            StoryThreadEffectOperations.Clue,
            TargetRef: null,
            Field: null,
            Delta: null,
            Value: "clue.fixture");

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(effect, WebJson));

        Assert.Equal(
            ["op", "value"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void MissingRequiredExpiryFlagIsRejectedDuringDeserialization()
    {
        const string json = """
            {
              "schemaVersion": "1.0.0",
              "threadId": "thr.fixture.dto",
              "templateId": "assoc.fixture.v1",
              "playerId": "p.fixture",
              "resolvedParams": {},
              "injections": [],
              "mediaRefs": [],
              "auditRef": "genjob.fixture",
              "fallbackUsed": false
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StoryThreadContract>(json, WebJson));
    }

    [Fact]
    public void UnknownNestedEffectMemberIsRejectedDuringDeserialization()
    {
        const string json = """
            {
              "op": "clue",
              "value": "clue.fixture",
              "providerPayload": "forbidden"
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StoryThreadEffectContract>(json, WebJson));
    }
}
