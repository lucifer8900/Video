using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationDecisionServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpaqueTokenSelectionIsMaterializedFromApprovedServerData()
    {
        var ranker = new MockAssociationRanker(
            """
            {
              "schemaVersion": "1.0.0",
              "proposals": [
                {
                  "candidateToken": "cand.0000",
                  "score": 0.95,
                  "variantToken": "var.mention.controlled",
                  "effectSelections": [{ "effectIndex": 0 }]
                }
              ]
            }
            """);
        AssociationDecisionService service = CreateService(ranker);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { ApprovedTemplate() },
            CancellationToken.None);

        Assert.False(result.FallbackUsed);
        Assert.Equal("assoc.enemy.echo.v1", result.TemplateId);
        Assert.Equal("npc.known", result.ResolvedParams["target"]);
        StoryThreadInjectionContract injection = Assert.Single(result.Injections);
        Assert.Equal("node.next", injection.NodeId);
        Assert.Equal("[approved-test-variant]", injection.Text);
        Assert.Equal("clue.known", Assert.Single(injection.Effects).Value);
        Assert.Equal(new[] { "cue.approved.ambient" }, result.MediaRefs);
        Assert.Equal(1, ranker.CallCount);

        string providerRequest = JsonSerializer.Serialize(Assert.Single(ranker.Requests));
        Assert.DoesNotContain("assoc.enemy.echo.v1", providerRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("npc.known", providerRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("node.next", providerRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("[approved-test-variant]", providerRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("clue.known", providerRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("cue.approved.ambient", providerRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankerReturningTemplateTextFullEffectsOrMediaIsRejectedToApprovedFallback()
    {
        var ranker = new MockAssociationRanker(
            """
            {
              "schemaVersion": "1.0.0",
              "proposals": [
                {
                  "candidateToken": "cand.0000",
                  "score": 1,
                  "variantToken": "var.mention.controlled",
                  "effectSelections": [],
                  "templateId": "assoc.enemy.echo.v1",
                  "resolvedParams": { "target": "npc.known" },
                  "nodeId": "node.next",
                  "text": "unapproved free text",
                  "effects": [{ "op": "clue", "value": "clue.known" }],
                  "mediaRefs": ["cue.unapproved"]
                }
              ]
            }
            """);
        AssociationDecisionService service = CreateService(ranker);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { ApprovedTemplate() },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal("assoc.fallback.default.v1", result.TemplateId);
        StoryThreadInjectionContract injection = Assert.Single(result.Injections);
        Assert.Equal("node.next", injection.NodeId);
        Assert.Equal("[approved-test-fallback]", injection.Text);
        Assert.Equal(1, ranker.CallCount);
    }

    [Fact]
    public async Task WholeInvalidJsonUsesApprovedFallback()
    {
        var ranker = new MockAssociationRanker("{ not-json");
        AssociationDecisionService service = CreateService(ranker);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { ApprovedTemplate() },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal("assoc.fallback.default.v1", result.TemplateId);
        Assert.NotEmpty(result.Injections);
        Assert.Equal(1, ranker.CallCount);
    }

    private static AssociationDecisionService CreateService(IAssociationRanker ranker)
    {
        ApprovedTextVariantRegistry variants = ApprovedTextVariantRegistry.CreateForTests(new[]
        {
            new ApprovedTextVariantFixture(
                "assoc.enemy.echo.v1",
                "var.mention.controlled",
                StoryThreadInjectionPoints.NpcMention,
                "[approved-test-variant]",
                new[] { "cue.approved.ambient" }),
        });
        var guard = new AssociationConsistencyGuard(
            new AssociationEntityRegistry(
                Array.Empty<string>(),
                new[] { "chapter.red_mist" },
                new[] { "clue.known" },
                Array.Empty<string>(),
                new[]
                {
                    "assoc.enemy.echo.v1",
                    "char.player",
                    "npc.known",
                    "loc.next",
                    "node.next",
                    "node.previous",
                    "cue.approved.ambient",
                    "assoc.fallback.default.v1",
                    "cue.approved.fallback",
                }),
            variants,
            new FixedAssociationWorldStateProvider(new AssociationWorldSnapshot(
                "snapshot.service-tests.1",
                new[]
                {
                    new AssociationNpcWorldState(
                        "npc.known",
                        AssociationNpcLifeState.Alive,
                        "loc.next"),
                },
                new[] { "loc.next" })),
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics(),
            new InMemoryAssociationTriggerGate());
        return new AssociationDecisionService(
            new AssociationRuleFilter(),
            ranker,
            variants,
            ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
            {
                new ApprovedFallbackStoryThreadFixture(
                    "assoc.fallback.default.v1",
                    StoryThreadInjectionPoints.NpcMention,
                    "[approved-test-fallback]",
                    Array.Empty<StoryThreadEffectContract>(),
                    new[] { "cue.approved.fallback" }),
            }),
            guard,
            new SequenceAssociationDecisionIdFactory("thr.test", "genjob.test"),
            new FixedTimeProvider(FixedNow));
    }

    private static AssociationDecisionRequest Request() => new(
        PlayerId: "p.known",
        Chapter: "chapter.red_mist",
        WorldClock: 2000,
        Route: new AssociationRouteContext(
            "node.next",
            "loc.next",
            new[] { StoryThreadInjectionPoints.NpcMention }),
        ActiveFactIds: Array.Empty<string>(),
        LedgerEntries: new[]
        {
            new NarrativeLedgerEntry(
                new LedgerEventData(
                    "1.0.0",
                    "led.known",
                    "p.known",
                    LedgerEventTypes.EnemySpared,
                    new[] { "char.player", "npc.known" },
                    3,
                    "chapter.red_mist",
                    1500,
                    "node.previous",
                    Array.Empty<string>(),
                    Parse("{}")),
                FixedNow,
                1),
        },
        TriggerHistory: Array.Empty<AssociationTriggerHistory>(),
        DefaultFallbackThreadId: "assoc.fallback.default.v1");

    private static ApprovedAssociationTemplate ApprovedTemplate()
    {
        var contract = new AssociationTemplateContract(
            "1.0.0",
            "assoc.enemy.echo.v1",
            AssociationKinds.Callback,
            "L2",
            new AssociationPreconditionsContract(
                new[]
                {
                    new AssociationLedgerRequirementContract(
                        LedgerEventTypes.EnemySpared,
                        2,
                        1000),
                },
                Array.Empty<string>(),
                new[] { "chapter.red_mist" }),
            new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["target"] = new("ledger.actors[1]"),
            },
            new[] { StoryThreadInjectionPoints.NpcMention },
            "llm_variant_within_style_guide",
            new[]
            {
                new AssociationAllowedEffectContract(
                    AssociationEffectOperations.Clue,
                    null,
                    null,
                    "clue.known"),
            },
            AssociationMediaPolicies.ReuseOnly,
            "assoc.fallback.default.v1",
            100,
            1,
            "approved");
        return new ApprovedAssociationTemplate("test-template.json", contract);
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
