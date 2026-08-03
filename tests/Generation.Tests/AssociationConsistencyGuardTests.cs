using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationConsistencyGuardTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequiredViolationFixturesAreRejectedAndCounted()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);

        AssociationConsistencyResult dead = guard.Evaluate(Input(
            world: World(AssociationNpcLifeState.Dead)));
        AssociationConsistencyResult unauthorized = guard.Evaluate(Input(
            draft: Draft(effects: new[]
            {
                new StoryThreadEffectContract(
                    StoryThreadEffectOperations.BranchUnlock,
                    null,
                    null,
                    null,
                    "branch.known"),
            })));
        AssociationConsistencyResult cooldown = guard.Evaluate(Input(
            request: Request(history: new[]
            {
                new AssociationTriggerHistory("assoc.enemy.echo.v1", 1_950, 0),
            })));
        AssociationConsistencyResult inventedProperNoun = guard.Evaluate(Input(
            draft: Draft(text: "[approved-test-variant] invented.proper_noun")));

        Assert.Equal(AssociationConsistencyRejectionReason.TargetDead, dead.Reason);
        Assert.Equal(AssociationConsistencyRejectionReason.EffectNotAllowed, unauthorized.Reason);
        Assert.Equal(AssociationConsistencyRejectionReason.CooldownActive, cooldown.Reason);
        Assert.Equal(AssociationConsistencyRejectionReason.UnapprovedTextVariant, inventedProperNoun.Reason);
        Assert.Equal(4, metrics.TotalRejected);
        Assert.Equal(1, metrics.GetCount(AssociationConsistencyRejectionReason.TargetDead));
        Assert.Equal(1, metrics.GetCount(AssociationConsistencyRejectionReason.EffectNotAllowed));
        Assert.Equal(1, metrics.GetCount(AssociationConsistencyRejectionReason.CooldownActive));
        Assert.Equal(1, metrics.GetCount(AssociationConsistencyRejectionReason.UnapprovedTextVariant));
    }

    [Fact]
    public void UnknownEntityAndUnreachableTargetFailClosed()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);
        AssociationDecisionRequest unknownNode = Request() with
        {
            Route = Request().Route with { UpcomingNodeId = "node.unregistered" },
        };

        AssociationConsistencyResult unknown = guard.Evaluate(Input(request: unknownNode));
        AssociationConsistencyResult unreachable = guard.Evaluate(Input(
            world: World(AssociationNpcLifeState.Alive, reachableLocations: Array.Empty<string>())));

        Assert.Equal(AssociationConsistencyRejectionReason.UnknownEntity, unknown.Reason);
        Assert.Equal(AssociationConsistencyRejectionReason.TargetUnreachable, unreachable.Reason);
        Assert.Equal(2, metrics.TotalRejected);
    }

    [Fact]
    public void RelationshipRangeAndTriggerLimitAreRecheckedBeforeSigning()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);
        ApprovedAssociationTemplate relationshipTemplate = Template(
            effect: new AssociationAllowedEffectContract(
                AssociationEffectOperations.Relationship,
                "trust",
                new[] { -1d, 1d },
                null));
        AssociationConsistencyInput rangeInput = Input(
            template: relationshipTemplate,
            draft: Draft(effects: new[]
            {
                new StoryThreadEffectContract(
                    StoryThreadEffectOperations.Relationship,
                    "npc.known",
                    "trust",
                    2d,
                    null),
            }));
        AssociationConsistencyInput countInput = Input(
            request: Request(history: new[]
            {
                new AssociationTriggerHistory("assoc.enemy.echo.v1", 1_000, 1),
            }));

        Assert.Equal(
            AssociationConsistencyRejectionReason.EffectOutOfRange,
            guard.Evaluate(rangeInput).Reason);
        Assert.Equal(
            AssociationConsistencyRejectionReason.TriggerLimitReached,
            guard.Evaluate(countInput).Reason);
    }

    [Fact]
    public void ForbiddenFactIsRecheckedImmediatelyBeforeSigning()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);
        AssociationDecisionRequest request = Request(
            activeFacts: new[] { "fact.route_closed" });
        ApprovedAssociationTemplate template = Template(
            forbidsFacts: new[] { "fact.route_closed" });

        AssociationConsistencyResult result = guard.Evaluate(Input(
            request: request,
            template: template));

        Assert.Equal(AssociationConsistencyRejectionReason.ForbiddenFact, result.Reason);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ForbiddenFact));
    }

    [Fact]
    public void ExactApprovedVariantAndBoundaryValuesPass()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);
        AssociationDecisionRequest request = Request(history: new[]
        {
            new AssociationTriggerHistory("assoc.enemy.echo.v1", 1_900, 0),
        });

        AssociationConsistencyResult result = guard.Evaluate(Input(request: request));

        Assert.True(result.Passed);
        Assert.Null(result.Reason);
        Assert.Equal(0, metrics.TotalRejected);
    }

    [Fact]
    public void ParameterizedAllowedEffectIsResolvedBeforeRegistryAndWhitelistChecks()
    {
        var metrics = new AssociationRejectionMetrics();
        AssociationConsistencyGuard guard = Guard(metrics);
        ApprovedAssociationTemplate template = Template(
            effect: new AssociationAllowedEffectContract(
                AssociationEffectOperations.Clue,
                null,
                null,
                "clue.echo_{target}"));
        AssociationThreadDraft draft = Draft(effects: new[]
        {
            new StoryThreadEffectContract(
                StoryThreadEffectOperations.Clue,
                null,
                null,
                null,
                "clue.echo_known"),
        });

        AssociationConsistencyResult result = guard.Evaluate(Input(
            template: template,
            draft: draft));

        Assert.True(result.Passed);
        Assert.Equal(0, metrics.TotalRejected);
    }

    [Fact]
    public void WorldSnapshotDeepFreezesCallerOwnedCollections()
    {
        var npcStates = new List<AssociationNpcWorldState>
        {
            new("npc.known", AssociationNpcLifeState.Alive, "loc.next"),
        };
        var reachable = new List<string> { "loc.next" };
        var facts = new List<string> { "fact.route_open" };
        var snapshot = new AssociationWorldSnapshot(
            "snapshot.freeze.1",
            npcStates,
            reachable,
            facts);

        npcStates[0] = new AssociationNpcWorldState(
            "npc.known",
            AssociationNpcLifeState.Dead,
            "loc.next");
        reachable.Clear();
        facts[0] = "fact.route_closed";

        AssociationNpcWorldState state = Assert.Single(snapshot.NpcStates);
        Assert.Equal(AssociationNpcLifeState.Alive, state.LifeState);
        Assert.Equal("loc.next", Assert.Single(snapshot.ReachableLocationIds));
        Assert.Equal("fact.route_open", Assert.Single(snapshot.ActiveFactIds));
    }

    [Fact]
    public async Task PendingReviewerTimeoutCancelsProviderAndLateResultCannotPass()
    {
        var metrics = new AssociationRejectionMetrics();
        var reviewer = new PendingReviewer();
        AssociationConsistencyGuard guard = Guard(
            metrics,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true));
        TimeProvider clock = TimeProvider.System;

        AssociationConsistencyResult result = await guard.ReviewAsync(
            Input(),
            clock.GetTimestamp(),
            TimeSpan.FromMilliseconds(50),
            clock,
            CancellationToken.None);

        Assert.Equal(AssociationConsistencyRejectionReason.ModelTimeout, result.Reason);
        Assert.True(reviewer.ProviderCancellation.IsCancellationRequested);
        reviewer.Complete(
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}");
        await Task.Yield();
        Assert.Equal(1, metrics.TotalRejected);
    }

    [Fact]
    public async Task CallerCancellationCancelsPendingReviewerWithoutMetric()
    {
        using var caller = new CancellationTokenSource();
        var metrics = new AssociationRejectionMetrics();
        var reviewer = new PendingReviewer();
        AssociationConsistencyGuard guard = Guard(
            metrics,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true));
        TimeProvider clock = TimeProvider.System;
        Task<AssociationConsistencyResult> pending = guard.ReviewAsync(
            Input(),
            clock.GetTimestamp(),
            TimeSpan.FromSeconds(5),
            clock,
            caller.Token);
        await reviewer.Called;

        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(reviewer.ProviderCancellation.IsCancellationRequested);
        Assert.Equal(0, metrics.TotalRejected);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}", true)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"reject\",\"reasonCode\":\"fact_conflict\"}", true)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"fact_conflict\"}", false)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"reject\",\"reasonCode\":\"none\"}", false)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"reject\",\"reasonCode\":\"other\"}", false)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\",\"detail\":\"forbidden\"}", false)]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"decision\":\"reject\",\"reasonCode\":\"none\"}", false)]
    public void ReviewWireProtocolIsStrict(string json, bool expected)
    {
        bool parsed = AssociationConsistencyReviewWireProtocol.TryParse(
            json,
            out AssociationConsistencyReviewResponse? response);

        Assert.Equal(expected, parsed);
        Assert.Equal(expected, response is not null);
    }

    private static AssociationConsistencyGuard Guard(
        AssociationRejectionMetrics metrics,
        IAssociationConsistencyReviewer? reviewer = null,
        AssociationConsistencyReviewOptions? options = null) =>
        new(
            Registry(),
            Variants(),
            new FixedAssociationWorldStateProvider(World(AssociationNpcLifeState.Alive)),
            reviewer ?? new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            options ?? AssociationConsistencyReviewOptions.Disabled,
            metrics,
            new InMemoryAssociationTriggerGate());

    private static AssociationConsistencyInput Input(
        AssociationDecisionRequest? request = null,
        ApprovedAssociationTemplate? template = null,
        AssociationThreadDraft? draft = null,
        AssociationWorldSnapshot? world = null)
    {
        AssociationDecisionRequest actualRequest = request ?? Request();
        ApprovedAssociationTemplate actualTemplate = template ?? Template();
        NarrativeLedgerEntry evidence = actualRequest.LedgerEntries[0];
        var candidate = new AssociationRuleCandidate(
            actualTemplate,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["target"] = "npc.known",
            },
            evidence,
            StoryThreadInjectionPoints.NpcMention);
        return new AssociationConsistencyInput(
            actualRequest,
            candidate,
            draft ?? Draft(),
            world ?? World(AssociationNpcLifeState.Alive));
    }

    private static AssociationThreadDraft Draft(
        string text = "[approved-test-variant]",
        IReadOnlyList<StoryThreadEffectContract>? effects = null) =>
        new(
            "assoc.enemy.echo.v1",
            "var.mention.controlled",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["target"] = "npc.known",
            },
            new StoryThreadInjectionContract(
                "node.next",
                StoryThreadInjectionPoints.NpcMention,
                text,
                effects ?? new[]
                {
                    new StoryThreadEffectContract(
                        StoryThreadEffectOperations.Clue,
                        null,
                        null,
                        null,
                        "clue.known"),
                }),
            new[] { "cue.approved.ambient" });

    private static AssociationDecisionRequest Request(
        IReadOnlyList<AssociationTriggerHistory>? history = null,
        IReadOnlyList<string>? activeFacts = null) => new(
        "p.known",
        "chapter.red_mist",
        2_000,
        new AssociationRouteContext(
            "node.next",
            "loc.next",
            new[] { StoryThreadInjectionPoints.NpcMention }),
        activeFacts ?? Array.Empty<string>(),
        new[]
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
                    1_500,
                    "node.previous",
                    Array.Empty<string>(),
                    Parse("{}")),
                FixedNow,
                1),
        },
        history ?? Array.Empty<AssociationTriggerHistory>(),
        "assoc.fallback.default.v1");

    private static ApprovedAssociationTemplate Template(
        AssociationAllowedEffectContract? effect = null,
        IReadOnlyList<string>? forbidsFacts = null) =>
        new(
            "consistency-template.json",
            new AssociationTemplateContract(
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
                            1_000),
                    },
                    forbidsFacts ?? Array.Empty<string>(),
                    new[] { "chapter.red_mist" }),
                new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
                {
                    ["target"] = new("ledger.actors[1]"),
                },
                new[] { StoryThreadInjectionPoints.NpcMention },
                "llm_variant_within_style_guide",
                new[]
                {
                    effect ?? new AssociationAllowedEffectContract(
                        AssociationEffectOperations.Clue,
                        null,
                        null,
                        "clue.known"),
                },
                AssociationMediaPolicies.ReuseOnly,
                "assoc.fallback.default.v1",
                100,
                1,
                "approved"));

    private static AssociationEntityRegistry Registry() => new(
        factIds: new[] { "fact.route_open", "fact.route_closed" },
        chapterIds: new[] { "chapter.red_mist" },
        clueIds: new[] { "clue.known", "clue.echo_known" },
        branchIds: new[] { "branch.known" },
        entityIds: new[]
        {
            "assoc.enemy.echo.v1",
            "char.player",
            "npc.known",
            "loc.next",
            "node.next",
            "node.previous",
            "cue.approved.ambient",
        });

    private static ApprovedTextVariantRegistry Variants() =>
        ApprovedTextVariantRegistry.CreateForTests(new[]
        {
            new ApprovedTextVariantFixture(
                "assoc.enemy.echo.v1",
                "var.mention.controlled",
                StoryThreadInjectionPoints.NpcMention,
                "[approved-test-variant]",
                new[] { "cue.approved.ambient" }),
        });

    private static AssociationWorldSnapshot World(
        AssociationNpcLifeState lifeState,
        IReadOnlyCollection<string>? reachableLocations = null) =>
        new(
            "snapshot.1",
            new[]
            {
                new AssociationNpcWorldState("npc.known", lifeState, "loc.next"),
            },
            reachableLocations ?? new[] { "loc.next" });

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class PendingReviewer : IAssociationConsistencyReviewer
    {
        private readonly TaskCompletionSource<string?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken _providerCancellation;

        public Task Called => _called.Task;

        public AssociationProviderAuditProfile AuditProfile { get; } = new(
            "reviewer",
            "test.association-reviewer",
            "prompt.association-reviewer.test.v1",
            "model.association-reviewer.test.v1");

        public CancellationToken ProviderCancellation => _providerCancellation;

        public Task<string?> ReviewAsync(
            AssociationConsistencyReviewRequest request,
            CancellationToken cancellationToken)
        {
            _providerCancellation = cancellationToken;
            _called.TrySetResult();
            return _completion.Task;
        }

        public void Complete(string? response) => _completion.TrySetResult(response);
    }
}
