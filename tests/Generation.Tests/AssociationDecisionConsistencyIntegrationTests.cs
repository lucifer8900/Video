using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationDecisionConsistencyIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeadFirstCandidateIsRejectedAndSecondCandidateCanBeSigned()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(Array.Empty<string?>());
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(
                    Proposal("cand.0000", "var.dead.controlled"),
                    Proposal("cand.0001", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template("assoc.a.dead.v1", LedgerEventTypes.EnemySpared),
                Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued),
            },
            CancellationToken.None);

        Assert.False(result.FallbackUsed);
        Assert.Equal("assoc.b.alive.v1", result.TemplateId);
        Assert.Equal(1, metrics.GetCount(AssociationConsistencyRejectionReason.TargetDead));
        Assert.Equal(0, reviewer.CallCount);
    }

    [Fact]
    public async Task EnabledModelReviewRejectsToApprovedFallbackAndIsCountedOnce()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(new[]
        {
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"reject\",\"reasonCode\":\"character_conflict\"}",
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal("assoc.fallback.default.v1", result.TemplateId);
        Assert.Equal(1, reviewer.CallCount);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ModelCharacterConflict));
        AssociationConsistencyReviewRequest request = Assert.Single(reviewer.Requests);
        Assert.Equal("chapter.red_mist", request.ChapterId);
        Assert.Equal("assoc.b.alive.v1", request.TemplateId);
        Assert.Equal("npc.alive", request.TargetNpcId);
        Assert.DoesNotContain(
            request.GetType().GetProperties(),
            property => property.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Ledger", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Transcript", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Device", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DisabledModelReviewNeverCallsProvider()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(new[]
        {
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"reject\",\"reasonCode\":\"fact_conflict\"}",
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.False(result.FallbackUsed);
        Assert.Equal(0, reviewer.CallCount);
        Assert.Equal(0, metrics.TotalRejected);
    }

    [Fact]
    public async Task PrefilterCooldownRejectionUsesTheSameBoundedMetric()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var ranker = new MockAssociationRanker(Array.Empty<string?>());
        AssociationDecisionService service = Service(
            ranker,
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(history: new[]
            {
                new AssociationTriggerHistory("assoc.b.alive.v1", 1_950, 0),
            }),
            new[]
            {
                Template(
                    "assoc.b.alive.v1",
                    LedgerEventTypes.NpcRescued,
                    cooldownWorldClock: 100),
            },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(0, ranker.CallCount);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.CooldownActive));
    }

    [Fact]
    public async Task InvalidReviewResponseFailsClosedWithoutReviewingAnotherCandidate()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(new[]
        {
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"fact_conflict\"}",
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}",
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(
                    Proposal("cand.0000", "var.alive.controlled"),
                    Proposal("cand.0001", "var.second.controlled")),
            }),
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued),
                Template("assoc.c.second.v1", LedgerEventTypes.PromiseMade),
            },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, reviewer.CallCount);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ModelInvalidResponse));
    }

    [Fact]
    public async Task ReviewerCannotSignAfterSharedEightSecondDeadline()
    {
        var metrics = new AssociationRejectionMetrics();
        var clock = new ManualTimeProvider();
        ApprovedTextVariantRegistry variants = Variants();
        var ranker = new DelegateRanker((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromMilliseconds(7_900));
            return Task.FromResult<string?>(
                Response(Proposal("cand.0000", "var.alive.controlled")));
        });
        var reviewer = new DelegateReviewer((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromMilliseconds(100));
            return Task.FromResult<string?>(
                "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}");
        });
        AssociationDecisionService service = Service(
            ranker,
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics,
            clock);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, reviewer.CallCount);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ModelTimeout));
    }

    [Fact]
    public async Task WorldStateIsRecheckedAfterReviewerBeforeSigning()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(new[]
        {
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}",
        });
        var worldProvider = new SequenceWorldStateProvider(new[]
        {
            World(),
            new AssociationWorldSnapshot(
                "snapshot.2",
                new[]
                {
                    new AssociationNpcWorldState(
                        "npc.dead",
                        AssociationNpcLifeState.Dead,
                        "loc.dead"),
                    new AssociationNpcWorldState(
                        "npc.alive",
                        AssociationNpcLifeState.Dead,
                        "loc.next"),
                    new AssociationNpcWorldState(
                        "npc.second",
                        AssociationNpcLifeState.Alive,
                        "loc.next"),
                },
                new[] { "loc.next" }),
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics,
            worldStateProvider: worldProvider);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(2, worldProvider.CallCount);
        Assert.All(worldProvider.Queries, query =>
        {
            Assert.Equal("p.known", query.PlayerId);
            Assert.Equal("chapter.red_mist", query.ChapterId);
            Assert.Equal("node.next", query.UpcomingNodeId);
            Assert.Equal("loc.next", query.UpcomingLocationId);
        });
        Assert.Equal(1, reviewer.CallCount);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.TargetDead));
    }

    [Fact]
    public async Task NewlyActiveForbiddenFactIsRecheckedBeforeSigning()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var worldProvider = new SequenceWorldStateProvider(new[]
        {
            World(),
            World(activeFacts: new[] { "fact.route_closed" }),
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics,
            worldStateProvider: worldProvider);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template(
                    "assoc.b.alive.v1",
                    LedgerEventTypes.NpcRescued,
                    forbidsFacts: new[] { "fact.route_closed" }),
            },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ForbiddenFact));
    }

    [Fact]
    public async Task LedgerActorSlotCannotBypassDeadNpcCheckByUsingAnotherName()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.dead.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template(
                    "assoc.a.dead.v1",
                    LedgerEventTypes.EnemySpared,
                    actorSlotName: "speaker"),
            },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.TargetDead));
    }

    [Fact]
    public async Task NonNpcLedgerActorSlotDoesNotRequireNpcWorldState()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template(
                    "assoc.b.alive.v1",
                    LedgerEventTypes.NpcRescued,
                    actorSlotName: "speaker",
                    actorSourceIndex: 0),
            },
            CancellationToken.None);

        Assert.False(result.FallbackUsed);
        Assert.Equal("char.player", result.ResolvedParams["speaker"]);
        Assert.Equal(0, metrics.TotalRejected);
    }

    [Fact]
    public async Task FinalWorldRefreshCannotPushSigningPastEightSeconds()
    {
        var metrics = new AssociationRejectionMetrics();
        var clock = new ManualTimeProvider();
        ApprovedTextVariantRegistry variants = Variants();
        var ranker = new DelegateRanker((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromMilliseconds(7_900));
            return Task.FromResult<string?>(
                Response(Proposal("cand.0000", "var.alive.controlled")));
        });
        var worldProvider = new AdvancingWorldStateProvider(
            World(),
            clock,
            advanceOnCall: 2,
            TimeSpan.FromMilliseconds(100));
        AssociationDecisionService service = Service(
            ranker,
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics,
            clock,
            worldProvider);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ModelTimeout));
    }

    [Theory]
    [InlineData("unknown-variant", AssociationConsistencyRejectionReason.UnapprovedTextVariant)]
    [InlineData("unauthorized-effect", AssociationConsistencyRejectionReason.EffectNotAllowed)]
    [InlineData("out-of-range", AssociationConsistencyRejectionReason.EffectOutOfRange)]
    public async Task MaterializationRejectionsAreCountedOnTheRealServicePath(
        string violation,
        AssociationConsistencyRejectionReason expectedReason)
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        string variantToken = violation == "unknown-variant"
            ? "var.not.registered"
            : "var.alive.controlled";
        string selections = violation switch
        {
            "unauthorized-effect" => "[{\"effectIndex\":0}]",
            "out-of-range" =>
                "[{\"effectIndex\":0,\"relationshipDelta\":2}]",
            _ => "[]",
        };
        AssociationAllowedEffectContract? allowedEffect = violation == "out-of-range"
            ? new AssociationAllowedEffectContract(
                AssociationEffectOperations.Relationship,
                "trust",
                new[] { -1d, 1d },
                null)
            : null;
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", variantToken, selections)),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[]
            {
                Template(
                    "assoc.b.alive.v1",
                    LedgerEventTypes.NpcRescued,
                    allowedEffect: allowedEffect),
            },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, metrics.GetCount(expectedReason));
        Assert.Equal(1, metrics.TotalRejected);
    }

    [Fact]
    public async Task CallerCancellationDuringReviewerFaultIsNotCountedAsRejection()
    {
        using var caller = new CancellationTokenSource();
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new DelegateReviewer((_, _) =>
        {
            caller.Cancel();
            throw new InvalidOperationException("provider fault after caller cancellation");
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            caller.Token));

        Assert.Equal(0, metrics.TotalRejected);
    }

    [Fact]
    public async Task SharedTriggerGatePreventsTwoThreadsExceedingTheLimit()
    {
        var metrics = new AssociationRejectionMetrics();
        var gate = new InMemoryAssociationTriggerGate();
        ApprovedTextVariantRegistry variants = Variants();
        AssociationDecisionService firstService = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics,
            triggerGate: gate);
        AssociationDecisionService secondService = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics,
            triggerGate: gate);
        ApprovedAssociationTemplate template = Template(
            "assoc.b.alive.v1",
            LedgerEventTypes.NpcRescued);

        StoryThreadContract[] results = await Task.WhenAll(
            Task.Run(() => firstService.DecideAsync(
                Request(),
                new[] { template },
                CancellationToken.None)),
            Task.Run(() => secondService.DecideAsync(
                Request(),
                new[] { template },
                CancellationToken.None)));

        Assert.Single(results.Where(result => !result.FallbackUsed));
        Assert.Single(results.Where(result => result.FallbackUsed));
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.TriggerLimitReached));
    }

    [Fact]
    public async Task TriggerReservationRollsBackWhenSigningFails()
    {
        var gate = new InMemoryAssociationTriggerGate();
        ApprovedTextVariantRegistry variants = Variants();
        AssociationDecisionService failing = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics(),
            triggerGate: gate,
            idFactory: new InvalidIdFactory());

        await Assert.ThrowsAsync<AssociationDecisionConfigurationException>(() =>
            failing.DecideAsync(
                Request(),
                new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
                CancellationToken.None));

        AssociationDecisionService retry = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics(),
            triggerGate: gate);
        StoryThreadContract result = await retry.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public async Task SlowTriggerGateCannotPushSigningPastEightSeconds()
    {
        var metrics = new AssociationRejectionMetrics();
        var clock = new ManualTimeProvider();
        var gate = new AdvancingTriggerGate(clock, TimeSpan.FromMilliseconds(100));
        ApprovedTextVariantRegistry variants = Variants();
        var ranker = new DelegateRanker((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromMilliseconds(7_900));
            return Task.FromResult<string?>(
                Response(Proposal("cand.0000", "var.alive.controlled")));
        });
        AssociationDecisionService service = Service(
            ranker,
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            metrics,
            clock,
            triggerGate: gate);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, metrics.GetCount(
            AssociationConsistencyRejectionReason.ModelTimeout));
    }

    [Fact]
    public async Task ReviewerFactContextDriftFailsClosedWithoutASecondModelCall()
    {
        var metrics = new AssociationRejectionMetrics();
        ApprovedTextVariantRegistry variants = Variants();
        var reviewer = new MockAssociationConsistencyReviewer(new[]
        {
            "{\"schemaVersion\":\"1.0.0\",\"decision\":\"pass\",\"reasonCode\":\"none\"}",
        });
        var worldProvider = new SequenceWorldStateProvider(new[]
        {
            World(),
            World(activeFacts: new[] { "fact.review_context_changed" }),
        });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[]
            {
                Response(Proposal("cand.0000", "var.alive.controlled")),
            }),
            variants,
            reviewer,
            new AssociationConsistencyReviewOptions(enableModelReview: true),
            metrics,
            worldStateProvider: worldProvider);

        StoryThreadContract result = await service.DecideAsync(
            Request(),
            new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(1, reviewer.CallCount);
        Assert.Equal(1, metrics.TotalRejected);
    }

    [Fact]
    public async Task ApprovedFallbackStillRequiresRegisteredEntityReferences()
    {
        ApprovedTextVariantRegistry variants = Variants();
        ApprovedFallbackStoryThreadCatalog fallbacks =
            ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
            {
                new ApprovedFallbackStoryThreadFixture(
                    "assoc.fallback.default.v1",
                    StoryThreadInjectionPoints.NpcMention,
                    "[approved-test-fallback]",
                    Array.Empty<StoryThreadEffectContract>(),
                    new[] { "cue.unregistered.fallback" }),
            });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[] { "{ not-json" }),
            variants,
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics(),
            fallbacks: fallbacks);

        await Assert.ThrowsAsync<AssociationDecisionConfigurationException>(() =>
            service.DecideAsync(
                Request(),
                new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
                CancellationToken.None));
    }

    [Fact]
    public async Task FallbackCannotInjectIntoAnUnregisteredRouteNode()
    {
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[] { "{ not-json" }),
            Variants(),
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics());
        AssociationDecisionRequest request = Request() with
        {
            Route = Request().Route with { UpcomingNodeId = "node.unregistered" },
        };

        await Assert.ThrowsAsync<AssociationDecisionConfigurationException>(() =>
            service.DecideAsync(
                request,
                new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
                CancellationToken.None));
    }

    [Fact]
    public async Task SafeFallbackCannotApplyNpcRelationshipEffectsWithoutWorldGuard()
    {
        ApprovedFallbackStoryThreadCatalog fallbacks =
            ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
            {
                new ApprovedFallbackStoryThreadFixture(
                    "assoc.fallback.default.v1",
                    StoryThreadInjectionPoints.NpcMention,
                    "[approved-test-fallback]",
                    new[]
                    {
                        new StoryThreadEffectContract(
                            StoryThreadEffectOperations.Relationship,
                            "npc.dead",
                            "trust",
                            1,
                            null),
                    },
                    new[] { "cue.approved.fallback" }),
            });
        AssociationDecisionService service = Service(
            new MockAssociationRanker(new[] { "{ not-json" }),
            Variants(),
            new MockAssociationConsistencyReviewer(Array.Empty<string?>()),
            AssociationConsistencyReviewOptions.Disabled,
            new AssociationRejectionMetrics(),
            fallbacks: fallbacks);

        await Assert.ThrowsAsync<AssociationDecisionConfigurationException>(() =>
            service.DecideAsync(
                Request(),
                new[] { Template("assoc.b.alive.v1", LedgerEventTypes.NpcRescued) },
                CancellationToken.None));
    }

    private static AssociationDecisionService Service(
        IAssociationRanker ranker,
        ApprovedTextVariantRegistry variants,
        IAssociationConsistencyReviewer reviewer,
        AssociationConsistencyReviewOptions options,
        AssociationRejectionMetrics metrics,
        TimeProvider? timeProvider = null,
        IAssociationWorldStateProvider? worldStateProvider = null,
        ApprovedFallbackStoryThreadCatalog? fallbacks = null,
        IAssociationTriggerGate? triggerGate = null,
        IAssociationDecisionIdFactory? idFactory = null)
    {
        var guard = new AssociationConsistencyGuard(
            Registry(),
            variants,
            worldStateProvider ?? new FixedAssociationWorldStateProvider(World()),
            reviewer,
            options,
            metrics,
            triggerGate ?? new InMemoryAssociationTriggerGate());
        return new AssociationDecisionService(
            new AssociationRuleFilter(),
            ranker,
            variants,
            fallbacks ?? Fallbacks(),
            guard,
            idFactory ?? new SequenceAssociationDecisionIdFactory(
                "thr.consistency",
                "genjob.consistency"),
            timeProvider ?? TimeProvider.System);
    }

    private static AssociationDecisionRequest Request(
        IReadOnlyList<AssociationTriggerHistory>? history = null) => new(
        "p.known",
        "chapter.red_mist",
        2_000,
        new AssociationRouteContext(
            "node.next",
            "loc.next",
            new[] { StoryThreadInjectionPoints.NpcMention }),
        Array.Empty<string>(),
        new[]
        {
            Ledger("led.dead", LedgerEventTypes.EnemySpared, "npc.dead", 1),
            Ledger("led.alive", LedgerEventTypes.NpcRescued, "npc.alive", 2),
            Ledger("led.second", LedgerEventTypes.PromiseMade, "npc.second", 3),
        },
        history ?? Array.Empty<AssociationTriggerHistory>(),
        "assoc.fallback.default.v1");

    private static NarrativeLedgerEntry Ledger(
        string entryId,
        string type,
        string npcId,
        long ingestSequence) =>
        new(
            new LedgerEventData(
                "1.0.0",
                entryId,
                "p.known",
                type,
                new[] { "char.player", npcId },
                3,
                "chapter.red_mist",
                1_500 + ingestSequence,
                "node.previous",
                Array.Empty<string>(),
                Payload(type, npcId)),
            FixedNow,
            ingestSequence);

    private static JsonElement Payload(string type, string npcId)
    {
        string json = type switch
        {
            LedgerEventTypes.NpcRescued => $"{{\"npcRef\":\"{npcId}\"}}",
            LedgerEventTypes.PromiseMade => "{\"promiseRef\":\"promise.known\"}",
            _ => "{}",
        };
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ApprovedAssociationTemplate Template(
        string id,
        string ledgerType,
        long cooldownWorldClock = 0,
        string actorSlotName = "target",
        int actorSourceIndex = 1,
        AssociationAllowedEffectContract? allowedEffect = null,
        IReadOnlyList<string>? forbidsFacts = null) =>
        new(
            "integration-template.json",
            new AssociationTemplateContract(
                "1.0.0",
                id,
                AssociationKinds.Callback,
                "L2",
                new AssociationPreconditionsContract(
                    new[] { new AssociationLedgerRequirementContract(ledgerType, 2, 1_000) },
                    forbidsFacts ?? Array.Empty<string>(),
                    new[] { "chapter.red_mist" }),
                new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
                {
                    [actorSlotName] = new($"ledger.actors[{actorSourceIndex}]"),
                },
                new[] { StoryThreadInjectionPoints.NpcMention },
                "llm_variant_within_style_guide",
                allowedEffect is null
                    ? Array.Empty<AssociationAllowedEffectContract>()
                    : new[] { allowedEffect },
                AssociationMediaPolicies.ReuseOnly,
                "assoc.fallback.default.v1",
                cooldownWorldClock,
                1,
                "approved"));

    private static ApprovedTextVariantRegistry Variants() =>
        ApprovedTextVariantRegistry.CreateForTests(new[]
        {
            Variant("assoc.a.dead.v1", "var.dead.controlled"),
            Variant("assoc.b.alive.v1", "var.alive.controlled"),
            Variant("assoc.c.second.v1", "var.second.controlled"),
        });

    private static ApprovedTextVariantFixture Variant(string templateId, string token) =>
        new(
            templateId,
            token,
            StoryThreadInjectionPoints.NpcMention,
            "[approved-test-variant]",
            new[] { "cue.approved.ambient" });

    private static ApprovedFallbackStoryThreadCatalog Fallbacks() =>
        ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
        {
            new ApprovedFallbackStoryThreadFixture(
                "assoc.fallback.default.v1",
                StoryThreadInjectionPoints.NpcMention,
                "[approved-test-fallback]",
                Array.Empty<StoryThreadEffectContract>(),
                new[] { "cue.approved.fallback" }),
        });

    private static AssociationEntityRegistry Registry() => new(
        factIds: new[] { "fact.route_closed", "fact.review_context_changed" },
        chapterIds: new[] { "chapter.red_mist" },
        clueIds: Array.Empty<string>(),
        branchIds: Array.Empty<string>(),
        entityIds: new[]
        {
            "assoc.a.dead.v1",
            "assoc.b.alive.v1",
            "assoc.c.second.v1",
            "char.player",
            "npc.dead",
            "npc.alive",
            "npc.second",
            "loc.dead",
            "loc.next",
            "node.next",
            "node.previous",
            "cue.approved.ambient",
            "assoc.fallback.default.v1",
            "cue.approved.fallback",
            "promise.known",
        });

    private static AssociationWorldSnapshot World(
        IReadOnlyList<string>? activeFacts = null) =>
        new(
            "snapshot.1",
            new[]
            {
                new AssociationNpcWorldState(
                    "npc.dead",
                    AssociationNpcLifeState.Dead,
                    "loc.dead"),
                new AssociationNpcWorldState(
                    "npc.alive",
                    AssociationNpcLifeState.Alive,
                    "loc.next"),
                new AssociationNpcWorldState(
                    "npc.second",
                    AssociationNpcLifeState.Alive,
                    "loc.next"),
            },
            new[] { "loc.next" },
            activeFacts ?? Array.Empty<string>());

    private static string Response(params string[] proposals) =>
        "{\"schemaVersion\":\"1.0.0\",\"proposals\":[" +
        string.Join(',', proposals) + "]}";

    private static string Proposal(
        string candidateToken,
        string variantToken,
        string selections = "[]") =>
        $"{{\"candidateToken\":\"{candidateToken}\",\"score\":1,\"variantToken\":\"{variantToken}\",\"effectSelections\":{selections}}}";

    private sealed class DelegateRanker(
        Func<AssociationRankerRequest, CancellationToken, Task<string?>> handler) :
        IAssociationRanker
    {
        public Task<string?> RankAsync(
            AssociationRankerRequest request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class DelegateReviewer(
        Func<AssociationConsistencyReviewRequest, CancellationToken, Task<string?>> handler) :
        IAssociationConsistencyReviewer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<string?> ReviewAsync(
            AssociationConsistencyReviewRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return handler(request, cancellationToken);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan amount) =>
            Interlocked.Add(ref _timestamp, amount.Ticks);
    }

    private sealed class SequenceWorldStateProvider(
        IEnumerable<AssociationWorldSnapshot> snapshots) : IAssociationWorldStateProvider
    {
        private readonly Queue<AssociationWorldSnapshot> _snapshots = new(snapshots);
        private readonly List<AssociationWorldStateQuery> _queries = new();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public IReadOnlyList<AssociationWorldStateQuery> Queries =>
            Array.AsReadOnly(_queries.ToArray());

        public bool TryCapture(
            AssociationWorldStateQuery query,
            out AssociationWorldSnapshot snapshot)
        {
            Interlocked.Increment(ref _callCount);
            _queries.Add(query);
            if (_snapshots.Count == 0)
            {
                snapshot = null!;
                return false;
            }

            snapshot = _snapshots.Dequeue();
            return true;
        }
    }

    private sealed class AdvancingWorldStateProvider(
        AssociationWorldSnapshot snapshot,
        ManualTimeProvider clock,
        int advanceOnCall,
        TimeSpan amount) : IAssociationWorldStateProvider
    {
        private int _callCount;

        public bool TryCapture(
            AssociationWorldStateQuery query,
            out AssociationWorldSnapshot captured)
        {
            int call = Interlocked.Increment(ref _callCount);
            if (call == advanceOnCall)
            {
                clock.Advance(amount);
            }

            captured = snapshot;
            return true;
        }
    }

    private sealed class AdvancingTriggerGate(
        ManualTimeProvider clock,
        TimeSpan amount) : IAssociationTriggerGate
    {
        private readonly InMemoryAssociationTriggerGate _inner = new();

        public AssociationTriggerGateResult TryReserve(AssociationTriggerGateRequest request)
        {
            clock.Advance(amount);
            return _inner.TryReserve(request);
        }
    }

    private sealed class InvalidIdFactory : IAssociationDecisionIdFactory
    {
        public string CreateThreadId() => "invalid";

        public string CreateAuditId() => "invalid";
    }
}
