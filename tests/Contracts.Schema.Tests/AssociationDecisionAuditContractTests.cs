using System.Text.Json;
using System.Text.Json.Serialization;
using Lingmai.RedMist.Contracts.Associations;

namespace Contracts.Schema.Tests;

public sealed class AssociationDecisionAuditContractTests
{
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [Fact]
    public void PrimaryAuditStrictlyDeserializesWithoutSensitivePayloadFields()
    {
        string json = ReadFixture("valid/association-decision-audit.primary.json");

        AssociationDecisionAuditContract? audit =
            JsonSerializer.Deserialize<AssociationDecisionAuditContract>(json, StrictOptions);

        Assert.NotNull(audit);
        Assert.Equal("genjob.audit.primary.1", audit!.AuditRef);
        Assert.Equal("assoc.enemy.echo.v1", audit.TemplateId);
        Assert.Equal("npc.known", audit.ResolvedParams["target"]);
        AssociationProviderAuditContract provider = Assert.Single(audit.Providers);
        Assert.Equal("prompt.association-ranker.mock.v1", provider.PromptVersion);
        Assert.Equal("model.association-ranker.mock.v1", provider.ModelSnapshot);
        Assert.False(audit.FallbackUsed);
        Assert.Null(audit.OutcomeReason);
        Assert.DoesNotContain("rawPrompt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("providerResponse", json, StringComparison.Ordinal);
        Assert.DoesNotContain("injectionText", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FallbackAuditCarriesAStableReasonAndConfiguredProviderSnapshot()
    {
        AssociationDecisionAuditContract? audit =
            JsonSerializer.Deserialize<AssociationDecisionAuditContract>(
                ReadFixture("valid/association-decision-audit.fallback.json"),
                StrictOptions);

        Assert.NotNull(audit);
        Assert.True(audit!.FallbackUsed);
        Assert.Equal("no_candidates", audit.OutcomeReason);
        Assert.False(Assert.Single(audit.Providers).Invoked);
        Assert.NotEmpty(audit.GuardResults);
    }

    [Theory]
    [InlineData("invalid/association-decision-audit.missing-prompt-version.json")]
    [InlineData("invalid/association-decision-audit.missing-guard-results.json")]
    [InlineData("invalid/association-decision-audit.sensitive-additional-property.json")]
    public void MissingOrAdditionalFieldsAreRejectedByStrictDeserialization(string relativePath)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssociationDecisionAuditContract>(
                ReadFixture(relativePath),
                StrictOptions));
    }

    private static string ReadFixture(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "Video.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        return File.ReadAllText(Path.Combine(
            current.FullName,
            "tests",
            "StoryFixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
