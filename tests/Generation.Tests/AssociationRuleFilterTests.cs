using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationRuleFilterTests
{
    private const long Now = 2_000;

    [Fact]
    public void EligibleTemplatesAndResolvedSlotsAreReturnedInOrdinalOrder()
    {
        NarrativeLedgerEntry older = Ledger(
            entryId: "led.z",
            actors: new[] { "char.player", "npc.older" },
            worldClock: 1_800,
            ingestSequence: 20);
        NarrativeLedgerEntry newestBySequence = Ledger(
            entryId: "led.z-newest",
            actors: new[] { "char.player", "npc.sequence" },
            worldClock: 1_900,
            ingestSequence: 22);
        NarrativeLedgerEntry sameClockEarlierSequence = Ledger(
            entryId: "led.a-newest",
            actors: new[] { "char.player", "npc.not-selected" },
            worldClock: 1_900,
            ingestSequence: 21);
        AssociationDecisionRequest request = Request(
            ledgerEntries: new[] { older, newestBySequence, sameClockEarlierSequence },
            route: new AssociationRouteContext(
                "node.next",
                "loc.next",
                new[] { "travel_event", "npc_mention" }));
        ApprovedAssociationTemplate z = Template(
            templateId: "assoc.z.v1",
            slots: new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["target"] = new("ledger.actors[1]"),
                ["place"] = new("route.upcomingNode.location"),
            },
            injectionPoints: new[] { "travel_event", "npc_mention" });
        ApprovedAssociationTemplate a = Template(
            templateId: "assoc.a.v1",
            slots: new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["target"] = new("ledger.actors[1]"),
                ["place"] = new("route.upcomingNode.location"),
            },
            injectionPoints: new[] { "travel_event", "npc_mention" });

        IReadOnlyList<AssociationRuleCandidate> candidates =
            new AssociationRuleFilter().Filter(request, new[] { z, a });

        Assert.Equal(new[] { "assoc.a.v1", "assoc.z.v1" },
            candidates.Select(item => item.Template.Template.TemplateId));
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("led.z-newest", candidate.PrimaryEvidence.Event.EntryId);
            Assert.Equal(new[] { "place", "target" }, candidate.ResolvedParameters.Keys);
            Assert.Equal("loc.next", candidate.ResolvedParameters["place"]);
            Assert.Equal("npc.sequence", candidate.ResolvedParameters["target"]);
            Assert.Equal("npc_mention", candidate.InjectionPoint);
        });
    }

    [Fact]
    public void EveryLedgerRequirementMustHaveAnIndependentMatchingEntry()
    {
        AssociationLedgerRequirementContract[] requirements =
        {
            new(LedgerEventTypes.EnemySpared, 2, 1_000),
            new(LedgerEventTypes.PromiseMade, 1, 500),
        };
        ApprovedAssociationTemplate template = Template(requirements: requirements);
        AssociationDecisionRequest missingSecond = Request(
            ledgerEntries: new[] { Ledger() });

        Assert.Empty(new AssociationRuleFilter().Filter(missingSecond, new[] { template }));

        NarrativeLedgerEntry promise = Ledger(
            entryId: "led.promise",
            type: LedgerEventTypes.PromiseMade,
            severity: 1,
            worldClock: 1_500,
            ingestSequence: 2);
        AssociationDecisionRequest complete = Request(
            ledgerEntries: new[] { promise, Ledger() });

        AssociationRuleCandidate candidate = Assert.Single(
            new AssociationRuleFilter().Filter(complete, new[] { template }));
        Assert.Equal("led.valid", candidate.PrimaryEvidence.Event.EntryId);
    }

    [Theory]
    [InlineData("player")]
    [InlineData("chapter")]
    [InlineData("type")]
    [InlineData("severity")]
    [InlineData("future")]
    [InlineData("too-old")]
    public void LedgerEvidenceMustMatchEveryScopeAndTimeRule(string violation)
    {
        NarrativeLedgerEntry entry = violation switch
        {
            "player" => Ledger(playerId: "p.other"),
            "chapter" => Ledger(chapter: "chapter.other"),
            "type" => Ledger(type: LedgerEventTypes.PromiseMade),
            "severity" => Ledger(severity: 1),
            "future" => Ledger(worldClock: Now + 1),
            "too-old" => Ledger(worldClock: 999),
            _ => throw new InvalidOperationException(),
        };

        IReadOnlyList<AssociationRuleCandidate> candidates =
            new AssociationRuleFilter().Filter(Request(ledgerEntries: new[] { entry }), new[] { Template() });

        Assert.Empty(candidates);
    }

    [Fact]
    public void LedgerSeverityAndAgeInclusiveBoundariesAreAccepted()
    {
        NarrativeLedgerEntry boundary = Ledger(severity: 2, worldClock: 1_000);

        AssociationRuleCandidate candidate = Assert.Single(
            new AssociationRuleFilter().Filter(
                Request(ledgerEntries: new[] { boundary }),
                new[] { Template() }));

        Assert.Same(boundary, candidate.PrimaryEvidence);
    }

    [Fact]
    public void PlayerChapterAndTypeComparisonsAreOrdinal()
    {
        NarrativeLedgerEntry wrongCase = Ledger(
            playerId: "P.known",
            chapter: "Chapter.red_mist",
            type: "Enemy_Spared");
        AssociationDecisionRequest request = Request(
            playerId: "p.known",
            chapter: "chapter.red_mist",
            ledgerEntries: new[] { wrongCase });

        Assert.Empty(new AssociationRuleFilter().Filter(request, new[] { Template() }));
    }

    [Fact]
    public void ChapterWindowMustContainCurrentChapterUsingOrdinalComparison()
    {
        ApprovedAssociationTemplate wrongChapter = Template(
            chapterWindow: new[] { "Chapter.red_mist" });

        Assert.Empty(new AssociationRuleFilter().Filter(Request(), new[] { wrongChapter }));
    }

    [Fact]
    public void UnresolvableActorOrLocationSlotRejectsCandidate()
    {
        ApprovedAssociationTemplate actor = Template(
            templateId: "assoc.actor.v1",
            slots: new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["target"] = new("ledger.actors[7]"),
            });
        ApprovedAssociationTemplate location = Template(
            templateId: "assoc.location.v1",
            slots: new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["place"] = new("route.upcomingNode.location"),
            });
        AssociationDecisionRequest request = Request(
            route: new AssociationRouteContext("node.next", string.Empty, new[] { "npc_mention" }));

        Assert.Empty(new AssociationRuleFilter().Filter(request, new[] { location, actor }));
    }

    [Fact]
    public void DynamicForbiddenFactsAreResolvedBeforeOrdinalComparison()
    {
        ApprovedAssociationTemplate template = Template(
            slots: new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
            {
                ["target"] = new("ledger.actors[1]"),
            },
            forbidsFacts: new[] { "fact.{target}_dead" });
        AssociationDecisionRequest conflicting = Request(
            activeFacts: new[] { "fact.npc.known_dead" });

        Assert.Empty(new AssociationRuleFilter().Filter(conflicting, new[] { template }));

        AssociationDecisionRequest differentCase = Request(
            activeFacts: new[] { "Fact.npc.known_dead" });
        Assert.Single(new AssociationRuleFilter().Filter(differentCase, new[] { template }));
    }

    [Fact]
    public void LiteralForbiddenFactsAreEnforced()
    {
        ApprovedAssociationTemplate template = Template(
            forbidsFacts: new[] { "fact.route_closed" });

        Assert.Empty(new AssociationRuleFilter().Filter(
            Request(activeFacts: new[] { "fact.route_closed" }),
            new[] { template }));
    }

    [Theory]
    [InlineData(1_901, 0, false)]
    [InlineData(1_900, 0, true)]
    [InlineData(2_001, 0, false)]
    [InlineData(1_000, 2, false)]
    public void CooldownAndTriggerCountAreEnforced(long lastTriggered, int triggerCount, bool expected)
    {
        AssociationDecisionRequest request = Request(
            history: new[]
            {
                new AssociationTriggerHistory("assoc.valid.v1", lastTriggered, triggerCount),
            });

        IReadOnlyList<AssociationRuleCandidate> candidates =
            new AssociationRuleFilter().Filter(request, new[] { Template(cooldown: 100, maxTriggers: 2) });

        Assert.Equal(expected, candidates.Count == 1);
    }

    [Fact]
    public void TriggerHistoryTemplateIdComparisonIsOrdinalAndAllMatchingRowsAreEnforced()
    {
        AssociationDecisionRequest casingOnly = Request(
            history: new[] { new AssociationTriggerHistory("Assoc.valid.v1", Now, 9) });
        Assert.Single(new AssociationRuleFilter().Filter(casingOnly, new[] { Template() }));

        AssociationDecisionRequest duplicateRows = Request(
            history: new[]
            {
                new AssociationTriggerHistory("assoc.valid.v1", 1_000, 1),
                new AssociationTriggerHistory("assoc.valid.v1", 1_999, 0),
            });
        Assert.Empty(new AssociationRuleFilter().Filter(duplicateRows, new[] { Template() }));
    }

    [Fact]
    public void RouteMustOfferAnOrdinalIntersectionAndSelectionIsDeterministic()
    {
        ApprovedAssociationTemplate template = Template(
            injectionPoints: new[] { "travel_event", "npc_mention" });
        AssociationDecisionRequest none = Request(
            route: new AssociationRouteContext("node.next", "loc.next", new[] { "Npc_Mention" }));
        Assert.Empty(new AssociationRuleFilter().Filter(none, new[] { template }));

        AssociationDecisionRequest bothReverse = Request(
            route: new AssociationRouteContext(
                "node.next",
                "loc.next",
                new[] { "travel_event", "npc_mention" }));
        AssociationRuleCandidate candidate = Assert.Single(
            new AssociationRuleFilter().Filter(bothReverse, new[] { template }));
        Assert.Equal("npc_mention", candidate.InjectionPoint);
    }

    [Fact]
    public void RuntimeGenerationIsRejectedByDefaultAndRequiresExplicitPolicy()
    {
        ApprovedAssociationTemplate runtime = Template(
            mediaPolicy: AssociationMediaPolicies.AllowRuntimeGeneration);

        Assert.Empty(new AssociationRuleFilter().Filter(Request(), new[] { runtime }));

        AssociationDecisionRequest explicitlyAllowed = Request(
            runtimePolicy: AssociationRuntimePolicy.CreateForTests(allowRuntimeGeneration: true));
        Assert.Single(new AssociationRuleFilter().Filter(explicitlyAllowed, new[] { runtime }));
    }

    [Fact]
    public void NullTopLevelInputsAreRejected()
    {
        var filter = new AssociationRuleFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Filter(null!, new[] { Template() }));
        Assert.Throws<ArgumentNullException>(() => filter.Filter(Request(), null!));
    }

    private static AssociationDecisionRequest Request(
        string playerId = "p.known",
        string chapter = "chapter.red_mist",
        IReadOnlyList<string>? activeFacts = null,
        IReadOnlyList<NarrativeLedgerEntry>? ledgerEntries = null,
        IReadOnlyList<AssociationTriggerHistory>? history = null,
        AssociationRouteContext? route = null,
        AssociationRuntimePolicy? runtimePolicy = null) =>
        new(
            playerId,
            chapter,
            Now,
            route ?? new AssociationRouteContext("node.next", "loc.next", new[] { "npc_mention" }),
            activeFacts ?? Array.Empty<string>(),
            ledgerEntries ?? new[] { Ledger() },
            history ?? Array.Empty<AssociationTriggerHistory>(),
            "assoc.fallback.default.v1",
            runtimePolicy);

    private static ApprovedAssociationTemplate Template(
        string templateId = "assoc.valid.v1",
        IReadOnlyList<AssociationLedgerRequirementContract>? requirements = null,
        IReadOnlyList<string>? forbidsFacts = null,
        IReadOnlyList<string>? chapterWindow = null,
        IReadOnlyDictionary<string, AssociationParameterSlotContract>? slots = null,
        IReadOnlyList<string>? injectionPoints = null,
        string mediaPolicy = AssociationMediaPolicies.ReuseOnly,
        long cooldown = 100,
        int maxTriggers = 2)
    {
        var contract = new AssociationTemplateContract(
            "1.0.0",
            templateId,
            AssociationKinds.Callback,
            "L2",
            new AssociationPreconditionsContract(
                requirements ?? new[]
                {
                    new AssociationLedgerRequirementContract(
                        LedgerEventTypes.EnemySpared,
                        2,
                        1_000),
                },
                forbidsFacts ?? Array.Empty<string>(),
                chapterWindow ?? new[] { "chapter.red_mist" }),
            slots ?? new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal),
            injectionPoints ?? new[] { "npc_mention" },
            "llm_variant_within_style_guide",
            Array.Empty<AssociationAllowedEffectContract>(),
            mediaPolicy,
            "assoc.fallback.default.v1",
            cooldown,
            maxTriggers,
            "approved");
        return new ApprovedAssociationTemplate("test/" + templateId + ".json", contract);
    }

    private static NarrativeLedgerEntry Ledger(
        string entryId = "led.valid",
        string playerId = "p.known",
        string type = LedgerEventTypes.EnemySpared,
        IReadOnlyList<string>? actors = null,
        int severity = 3,
        string chapter = "chapter.red_mist",
        long worldClock = 1_500,
        long ingestSequence = 1)
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return new NarrativeLedgerEntry(
            new LedgerEventData(
                "1.0.0",
                entryId,
                playerId,
                type,
                actors ?? new[] { "char.player", "npc.known" },
                severity,
                chapter,
                worldClock,
                "node.previous",
                Array.Empty<string>(),
                document.RootElement.Clone()),
            DateTimeOffset.UnixEpoch,
            ingestSequence);
    }
}
