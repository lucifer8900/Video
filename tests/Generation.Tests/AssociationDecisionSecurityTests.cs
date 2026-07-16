using System.Reflection;
using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationDecisionSecurityTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvalidItemIsSkippedAndNextCandidateCanWin()
    {
        var ranker = new MockAssociationRanker(
            """
            {
              "schemaVersion": "1.0.0",
              "proposals": [
                {
                  "candidateToken": "cand.0000",
                  "score": 1,
                  "variantToken": "var.a.controlled",
                  "effectSelections": [],
                  "text": "provider text is forbidden"
                },
                {
                  "candidateToken": "cand.0001",
                  "score": 0.5,
                  "variantToken": "var.b.controlled",
                  "effectSelections": []
                }
              ]
            }
            """);

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
        Assert.False(result.FallbackUsed);
        Assert.Equal(1, ranker.CallCount);
    }

    public static TheoryData<string> InvalidCandidateTokenPayloads => new()
    {
        Response(
            Proposal("cand.unknown", "var.a.controlled"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
        Response(
            Proposal("CAND.0000", "var.a.controlled"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
        Response(
            Proposal("cand.0000", "var.a.controlled"),
            Proposal("cand.0000", "var.a.controlled", score: 0.9),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
    };

    [Theory]
    [MemberData(nameof(InvalidCandidateTokenPayloads))]
    public async Task UnknownCaseVariantOrDuplicateTokenIsSkipped(string rawJson)
    {
        var ranker = new MockAssociationRanker(rawJson);

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public async Task CaseFoldCollisionInvalidatesTheOtherwiseValidSiblingToken()
    {
        var ranker = new MockAssociationRanker(Response(
            Proposal("cand.0000", "var.a.controlled"),
            Proposal("CAND.0000", "var.a.controlled", score: 0.9),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)));

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
    }

    [Fact]
    public async Task CrossTemplateVariantCannotBeUsedForAnotherCandidate()
    {
        var ranker = new MockAssociationRanker(Response(
            Proposal("cand.0000", "var.b.controlled"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)));

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
    }

    [Fact]
    public async Task KnownCandidateUsesItsOwnApprovedFallbackWhenNoProposalCanMaterialize()
    {
        var ranker = new MockAssociationRanker(Response(
            Proposal("cand.0000", "var.not.registered")));
        ApprovedAssociationTemplate template = Template(
            "assoc.a.v1",
            fallbackThreadId: "assoc.fallback.a.v1");
        ApprovedFallbackStoryThreadCatalog fallbacks =
            ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
            {
                new ApprovedFallbackStoryThreadFixture(
                    "assoc.fallback.default.v1",
                    StoryThreadInjectionPoints.NpcMention,
                    "[approved-default-fallback]",
                    Array.Empty<StoryThreadEffectContract>(),
                    Array.Empty<string>()),
                new ApprovedFallbackStoryThreadFixture(
                    "assoc.fallback.a.v1",
                    StoryThreadInjectionPoints.NpcMention,
                    "[approved-candidate-fallback]",
                    Array.Empty<StoryThreadEffectContract>(),
                    Array.Empty<string>()),
            });

        StoryThreadContract result = await Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")),
                fallbacks)
            .DecideAsync(Request(), new[] { template }, default);

        Assert.True(result.FallbackUsed);
        Assert.Equal("assoc.fallback.a.v1", result.TemplateId);
        Assert.Equal("[approved-candidate-fallback]", Assert.Single(result.Injections).Text);
    }

    [Fact]
    public async Task InvalidScoreSkipsItemAndEvidenceAgeSentToRankerIsRelative()
    {
        var ranker = new MockAssociationRanker(
            """
            {
              "schemaVersion": "1.0.0",
              "proposals": [
                { "candidateToken": "cand.0000", "score": 2, "variantToken": "var.a.controlled", "effectSelections": [] },
                { "candidateToken": "cand.0001", "score": 0.5, "variantToken": "var.b.controlled", "effectSelections": [] }
              ]
            }
            """);

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
        Assert.All(Assert.Single(ranker.Requests).Candidates, candidate =>
            Assert.Equal(500, candidate.EvidenceAgeWorldClock));
    }

    public static TheoryData<string> InvalidEffectPayloads => new()
    {
        Response(
            Proposal("cand.0000", "var.a.controlled", "[{\"effectIndex\":99}]"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
        Response(
            Proposal("cand.0000", "var.a.controlled", "[{\"effectIndex\":0,\"relationshipDelta\":8}]"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
        Response(
            Proposal("cand.0000", "var.a.controlled", "[{\"effectIndex\":0},{\"effectIndex\":0}]"),
            Proposal("cand.0001", "var.b.controlled", score: 0.5)),
    };

    [Theory]
    [MemberData(nameof(InvalidEffectPayloads))]
    public async Task IllegalEffectIndexDeltaOrDuplicateSelectionSkipsCandidate(string rawJson)
    {
        var ranker = new MockAssociationRanker(rawJson);
        ApprovedAssociationTemplate relationship = Template(
            "assoc.a.v1",
            new AssociationAllowedEffectContract(
                AssociationEffectOperations.Relationship,
                "trust",
                new[] { -2d, 2d },
                null));

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { relationship, Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
    }

    [Fact]
    public async Task EmptyFilteredSetUsesFallbackWithoutCallingRanker()
    {
        var ranker = new MockAssociationRanker(Response(Proposal("cand.0000", "var.a.controlled")));
        ApprovedAssociationTemplate wrongChapter = Template(
            "assoc.a.v1",
            chapterWindow: new[] { "chapter.other" });

        StoryThreadContract result = await Service(ranker, Variants(Variant("assoc.a.v1", "var.a.controlled")))
            .DecideAsync(Request(), new[] { wrongChapter }, default);

        Assert.True(result.FallbackUsed);
        Assert.Equal(0, ranker.CallCount);
    }

    [Fact]
    public async Task MissingApprovedVariantUsesFallbackWithoutCallingRanker()
    {
        var ranker = new MockAssociationRanker(Response(Proposal("cand.0000", "var.a.controlled")));

        StoryThreadContract result = await Service(ranker, ApprovedTextVariantRegistry.Empty)
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);

        Assert.True(result.FallbackUsed);
        Assert.Equal(0, ranker.CallCount);
    }

    [Fact]
    public async Task NullSynchronousAndAsynchronousRankerFailuresUseFallback()
    {
        IAssociationRanker[] rankers =
        {
            new DelegateRanker((_, _) => Task.FromResult<string?>(null)),
            new DelegateRanker((_, _) => throw new InvalidOperationException("sync provider failure")),
            new DelegateRanker((_, _) => Task.FromException<string?>(new InvalidOperationException("async provider failure"))),
            new DelegateRanker((_, _) => null!),
        };

        foreach (IAssociationRanker ranker in rankers)
        {
            StoryThreadContract result = await Service(
                    ranker,
                    Variants(Variant("assoc.a.v1", "var.a.controlled")))
                .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);
            Assert.True(result.FallbackUsed);
        }
    }

    [Fact]
    public async Task CallerCancellationAfterInvalidProviderResponsePropagatesBeforeFallbackSigning()
    {
        using var caller = new CancellationTokenSource();
        var ids = new CountingIdFactory();
        var ranker = new DelegateRanker((_, _) =>
        {
            caller.Cancel();
            return Task.FromResult<string?>("{ not-json");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(
                    ranker,
                    Variants(Variant("assoc.a.v1", "var.a.controlled")),
                    idFactory: ids)
                .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, caller.Token));
        Assert.Equal(0, ids.ThreadIdCount);
        Assert.Equal(0, ids.AuditIdCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationTakesPrecedenceOverSynchronousOrAsynchronousProviderFault(
        bool asynchronous)
    {
        using var caller = new CancellationTokenSource();
        var ids = new CountingIdFactory();
        var ranker = new DelegateRanker((_, _) =>
        {
            caller.Cancel();
            if (!asynchronous) throw new InvalidOperationException("provider fault after caller cancellation");
            return Task.FromException<string?>(
                new InvalidOperationException("provider fault after caller cancellation"));
        });

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(
                    ranker,
                    Variants(Variant("assoc.a.v1", "var.a.controlled")),
                    idFactory: ids)
                .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(0, ids.ThreadIdCount);
        Assert.Equal(0, ids.AuditIdCount);
    }

    [Fact]
    public async Task CallerMutationsDuringRankerWaitCannotChangeServerOwnedMaterialization()
    {
        AssociationDecisionRequest request = Request();
        ApprovedAssociationTemplate template = Template("assoc.a.v1");
        var templates = new List<ApprovedAssociationTemplate> { template };
        var ranker = new PendingRanker();
        Task<StoryThreadContract> pending = Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")))
            .DecideAsync(request, templates, default);
        await ranker.Called;

        ((AssociationAllowedEffectContract[])template.Template.AllowedEffects)[0] =
            new AssociationAllowedEffectContract(
                AssociationEffectOperations.BranchUnlock,
                null,
                null,
                "branch.mutated");
        ((string[])request.LedgerEntries[0].Event.Actors)[1] = "npc.mutated";
        ((string[])request.Route.AllowedInjectionPoints)[0] = StoryThreadInjectionPoints.NodeIntro;
        templates.Clear();
        ranker.Complete(Response(Proposal(
            "cand.0000",
            "var.a.controlled",
            "[{\"effectIndex\":0}]")));

        StoryThreadContract result = await pending;
        Assert.False(result.FallbackUsed);
        Assert.Equal("npc.known", result.ResolvedParams["target"]);
        StoryThreadInjectionContract injection = Assert.Single(result.Injections);
        Assert.Equal(StoryThreadInjectionPoints.NpcMention, injection.Point);
        StoryThreadEffectContract effect = Assert.Single(injection.Effects);
        Assert.Equal(StoryThreadEffectOperations.Clue, effect.Op);
        Assert.Equal("clue.known", effect.Value);
    }

    [Fact]
    public async Task InvalidLedgerActorCannotEnterResolvedParametersOrReachRanker()
    {
        AssociationDecisionRequest baseline = Request();
        NarrativeLedgerEntry invalidEntry = baseline.LedgerEntries[0] with
        {
            Event = baseline.LedgerEntries[0].Event with
            {
                Actors = new[] { "char.player", "not valid id\n" },
            },
        };
        AssociationDecisionRequest request = baseline with
        {
            LedgerEntries = new[] { invalidEntry },
        };
        var ranker = new MockAssociationRanker(Response(Proposal(
            "cand.0000",
            "var.a.controlled",
            "[{\"effectIndex\":0}]")));
        var ids = new CountingIdFactory();

        await Assert.ThrowsAsync<AssociationDecisionValidationException>(() =>
            Service(
                    ranker,
                    Variants(Variant("assoc.a.v1", "var.a.controlled")),
                    idFactory: ids)
                .DecideAsync(request, new[] { Template("assoc.a.v1") }, default));
        Assert.Equal(0, ranker.CallCount);
        Assert.Equal(0, ids.ThreadIdCount);
        Assert.Equal(0, ids.AuditIdCount);
    }

    [Theory]
    [InlineData("actors-empty")]
    [InlineData("actors-nine")]
    [InlineData("actors-duplicate")]
    [InlineData("schema-version")]
    [InlineData("entry-id")]
    [InlineData("player")]
    [InlineData("event-type")]
    [InlineData("severity")]
    [InlineData("chapter")]
    [InlineData("world-clock")]
    [InlineData("source-node")]
    [InlineData("fact-prefix")]
    [InlineData("facts-seventeen")]
    [InlineData("facts-duplicate")]
    [InlineData("payload")]
    [InlineData("ingest-sequence")]
    public async Task InvalidLedgerContractBoundaryFailsBeforeRanker(string violation)
    {
        var ranker = new MockAssociationRanker(Response(Proposal(
            "cand.0000",
            "var.a.controlled")));

        await Assert.ThrowsAsync<AssociationDecisionValidationException>(() =>
            Service(ranker, Variants(Variant("assoc.a.v1", "var.a.controlled")))
                .DecideAsync(
                    RequestWithLedgerViolation(violation),
                    new[] { Template("assoc.a.v1") },
                    default));
        Assert.Equal(0, ranker.CallCount);
    }

    [Fact]
    public async Task FinalStoryThreadInvariantRejectsSchemaInvalidEffectFromBypassedTemplate()
    {
        ApprovedAssociationTemplate invalidTemplate = Template(
            "assoc.a.v1",
            new AssociationAllowedEffectContract(
                AssociationEffectOperations.Relationship,
                "not_a_relationship_field",
                new[] { -2d, 2d },
                null));
        var ranker = new MockAssociationRanker(Response(Proposal(
            "cand.0000",
            "var.a.controlled",
            "[{\"effectIndex\":0,\"relationshipDelta\":1}]")));

        StoryThreadContract result = await Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")))
            .DecideAsync(Request(), new[] { invalidTemplate }, default);

        Assert.True(result.FallbackUsed);
        Assert.Equal("assoc.fallback.default.v1", result.TemplateId);
    }

    [Fact]
    public async Task DuplicatePropertyInsideProposalOnlyRejectsThatItem()
    {
        var ranker = new MockAssociationRanker(
            """
            {
              "schemaVersion": "1.0.0",
              "proposals": [
                {
                  "candidateToken": "cand.0000",
                  "candidateToken": "cand.0000",
                  "score": 1,
                  "variantToken": "var.a.controlled",
                  "effectSelections": []
                },
                {
                  "candidateToken": "cand.0001",
                  "score": 0.5,
                  "variantToken": "var.b.controlled",
                  "effectSelections": []
                }
              ]
            }
            """);

        StoryThreadContract result = await Service(
                ranker,
                Variants(
                    Variant("assoc.a.v1", "var.a.controlled"),
                    Variant("assoc.b.v1", "var.b.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1"), Template("assoc.b.v1") }, default);

        Assert.Equal("assoc.b.v1", result.TemplateId);
    }

    public static TheoryData<string> InvalidWholePayloads
    {
        get
        {
            string tooLarge = "{\"schemaVersion\":\"1.0.0\",\"proposals\":[],\"padding\":\"" +
                              new string('x', AssociationRankerWireProtocol.MaximumResponseBytes) + "\"}";
            string tooDeep = "{\"schemaVersion\":\"1.0.0\",\"proposals\":" +
                             new string('[', AssociationRankerWireProtocol.MaximumJsonDepth + 2) +
                             new string(']', AssociationRankerWireProtocol.MaximumJsonDepth + 2) + "}";
            string tooMany = "{\"schemaVersion\":\"1.0.0\",\"proposals\":[" +
                             string.Join(',', Enumerable.Repeat("{}", AssociationRankerWireProtocol.MaximumProposalCount + 1)) + "]}";
            return new TheoryData<string>
            {
                "{\"schemaVersion\":\"1.0.0\",\"schemaVersion\":\"1.0.0\",\"proposals\":[]}",
                tooLarge,
                tooDeep,
                tooMany,
                "[]",
                "{ not-json",
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidWholePayloads))]
    public async Task DuplicateRootOrPayloadCapViolationUsesFallback(string rawJson)
    {
        StoryThreadContract result = await Service(
                new MockAssociationRanker(rawJson),
                Variants(Variant("assoc.a.v1", "var.a.controlled")))
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);

        Assert.True(result.FallbackUsed);
    }

    [Fact]
    public async Task CallerCancellationPropagatesCancelsProviderAndDoesNotSign()
    {
        var ranker = new PendingRanker();
        var ids = new CountingIdFactory();
        using var caller = new CancellationTokenSource();
        Task<StoryThreadContract> pending = Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")),
                idFactory: ids)
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, caller.Token);
        await ranker.Called;

        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(ranker.ProviderCancellation.IsCancellationRequested);
        Assert.Equal(0, ids.ThreadIdCount);
        Assert.Equal(0, ids.AuditIdCount);
    }

    [Fact]
    public async Task HardEightSecondDeadlineCancelsProviderFallsBackAndLateCompletionCannotSign()
    {
        var clock = new ManualTimeProvider(FixedNow);
        var ranker = new PendingRanker();
        var ids = new CountingIdFactory();
        Task<StoryThreadContract> pending = Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")),
                timeProvider: clock,
                idFactory: ids)
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);
        await ranker.Called;

        clock.Advance(TimeSpan.FromSeconds(8));

        StoryThreadContract result = await pending;
        Assert.True(result.FallbackUsed);
        Assert.True(ranker.ProviderCancellation.IsCancellationRequested);
        Assert.Equal(1, ids.ThreadIdCount);
        Assert.Equal(1, ids.AuditIdCount);

        ranker.Complete(Response(Proposal("cand.0000", "var.a.controlled")));
        await Task.Yield();
        Assert.Equal(1, ids.ThreadIdCount);
        Assert.Equal(1, ids.AuditIdCount);
    }

    [Fact]
    public async Task HardTimeoutIsExactlyEightSecondsAtOneTickBoundary()
    {
        Assert.Equal(TimeSpan.FromSeconds(8), AssociationDecisionService.HardTimeout);
        var clock = new ManualTimeProvider(FixedNow);
        var ranker = new PendingRanker();
        Task<StoryThreadContract> pending = Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")),
                timeProvider: clock)
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);
        await ranker.Called;

        clock.Advance(TimeSpan.FromSeconds(8) - TimeSpan.FromTicks(1));
        Assert.False(pending.IsCompleted);
        Assert.False(ranker.ProviderCancellation.IsCancellationRequested);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True((await pending).FallbackUsed);
        Assert.True(ranker.ProviderCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task ResponseCompletingAtExactDeadlineLosesTheRaceToHardTimeout()
    {
        var clock = new ManualTimeProvider(FixedNow);
        var ranker = new DelayedRanker(
            clock,
            TimeSpan.FromSeconds(8),
            Response(Proposal("cand.0000", "var.a.controlled")));
        Task<StoryThreadContract> pending = Service(
                ranker,
                Variants(Variant("assoc.a.v1", "var.a.controlled")),
                timeProvider: clock)
            .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);
        await ranker.Called;

        clock.Advance(TimeSpan.FromSeconds(8));

        Assert.True((await pending).FallbackUsed);
        Assert.True(ranker.ProviderCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task DeterministicMockLatencyHasP95BelowFiveSeconds()
    {
        var clock = new ManualTimeProvider(FixedNow);
        TimeSpan[] samples = Enumerable.Repeat(TimeSpan.FromSeconds(4), 95)
            .Concat(Enumerable.Repeat(TimeSpan.FromSeconds(7), 5))
            .ToArray();
        var observed = new List<TimeSpan>();

        foreach (TimeSpan latency in samples)
        {
            var ranker = new DelayedRanker(
                clock,
                latency,
                Response(Proposal("cand.0000", "var.a.controlled")));
            DateTimeOffset started = clock.GetUtcNow();
            Task<StoryThreadContract> pending = Service(
                    ranker,
                    Variants(Variant("assoc.a.v1", "var.a.controlled")),
                    timeProvider: clock)
                .DecideAsync(Request(), new[] { Template("assoc.a.v1") }, default);
            await ranker.Called;
            clock.Advance(latency);

            Assert.False((await pending).FallbackUsed);
            observed.Add(clock.GetUtcNow() - started);
        }

        TimeSpan p95 = observed
            .Order()
            .ElementAt((int)Math.Ceiling(observed.Count * 0.95) - 1);
        Assert.True(p95 < TimeSpan.FromSeconds(5), $"P95 was {p95}.");
    }

    [Theory]
    [InlineData("bad\ntext")]
    [InlineData("bad\u00adtext")]
    [InlineData("bad\u202etext")]
    [InlineData("bad\u2028text")]
    [InlineData("bad\ufefftext")]
    public void ApprovedVariantRegistryRejectsControlAndInvisibleFormattingCharacters(string text)
    {
        Assert.Throws<ArgumentException>(() => Variants(
            new ApprovedTextVariantFixture(
                "assoc.a.v1",
                "var.a.controlled",
                StoryThreadInjectionPoints.NpcMention,
                text,
                Array.Empty<string>())));
    }

    [Fact]
    public void ApprovedRegistriesDeepFreezeCallerOwnedLists()
    {
        var variantMedia = new List<string> { "cue.original" };
        ApprovedTextVariantRegistry variants = Variants(new ApprovedTextVariantFixture(
            "assoc.a.v1",
            "var.a.controlled",
            StoryThreadInjectionPoints.NpcMention,
            "[approved-test-variant]",
            variantMedia));
        variantMedia[0] = "cue.mutated";

        var effects = new List<StoryThreadEffectContract>
        {
            new(StoryThreadEffectOperations.Clue, null, null, null, "clue.original"),
        };
        var fallbackMedia = new List<string> { "cue.fallback.original" };
        ApprovedFallbackStoryThreadCatalog fallbacks = Fallbacks(effects, fallbackMedia);
        effects[0] = new StoryThreadEffectContract(
            StoryThreadEffectOperations.BranchUnlock,
            null,
            null,
            null,
            "branch.mutated");
        fallbackMedia[0] = "cue.fallback.mutated";

        Assert.Equal("cue.original", Assert.Single(Assert.Single(variants.Entries).MediaRefs));
        ApprovedFallbackStoryThread fallback = Assert.Single(fallbacks.Entries);
        Assert.Equal("clue.original", Assert.Single(fallback.Effects).Value);
        Assert.Equal("cue.fallback.original", Assert.Single(fallback.MediaRefs));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<string>)Assert.Single(variants.Entries).MediaRefs).Add("cue.injected"));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<StoryThreadEffectContract>)fallback.Effects).Add(
                new StoryThreadEffectContract(
                    StoryThreadEffectOperations.Clue,
                    null,
                    null,
                    null,
                    "clue.injected")));
    }

    public static TheoryData<StoryThreadEffectContract> InvalidFallbackEffects => new()
    {
        new StoryThreadEffectContract(StoryThreadEffectOperations.Clue, null, null, null, "branch.wrong"),
        new StoryThreadEffectContract(StoryThreadEffectOperations.BranchUnlock, null, null, null, "clue.wrong"),
        new StoryThreadEffectContract(StoryThreadEffectOperations.Relationship, "npc.known", "trust", double.NaN, null),
        new StoryThreadEffectContract("attack", "npc.known", null, null, null),
    };

    [Theory]
    [MemberData(nameof(InvalidFallbackEffects))]
    public void ApprovedFallbackCatalogRejectsSchemaInvalidOrTypeConfusedEffects(
        StoryThreadEffectContract effect)
    {
        Assert.Throws<ArgumentException>(() => Fallbacks(new[] { effect }, Array.Empty<string>()));
    }

    [Fact]
    public async Task UnknownOrCaseVariantFallbackIdFailsClosedWithoutFabricatingThread()
    {
        AssociationDecisionService service = Service(
            new MockAssociationRanker("{ not-json"),
            Variants(Variant("assoc.a.v1", "var.a.controlled")));

        await Assert.ThrowsAsync<AssociationDecisionConfigurationException>(() =>
            service.DecideAsync(
                Request() with { DefaultFallbackThreadId = "assoc.Fallback.default.v1" },
                new[] { Template("assoc.a.v1") },
                default));
    }

    [Fact]
    public async Task RequestCollectionsAndTemplateCollectionHaveHardCaps()
    {
        AssociationDecisionService service = Service(
            new MockAssociationRanker("{ not-json"),
            Variants(Variant("assoc.a.v1", "var.a.controlled")));
        var oversizedRequests = new[]
        {
            Request() with
            {
                ActiveFactIds = Enumerable.Range(0, 129).Select(index => $"fact.{index}").ToArray(),
            },
            Request() with
            {
                LedgerEntries = Enumerable.Repeat(Request().LedgerEntries[0], 513).ToArray(),
            },
            Request() with
            {
                TriggerHistory = Enumerable.Range(0, 257)
                    .Select(index => new AssociationTriggerHistory($"assoc.history.{index}", 0, 0))
                    .ToArray(),
            },
        };

        foreach (AssociationDecisionRequest request in oversizedRequests)
        {
            await Assert.ThrowsAsync<AssociationDecisionValidationException>(() =>
                service.DecideAsync(request, new[] { Template("assoc.a.v1") }, default));
        }

        ApprovedAssociationTemplate template = Template("assoc.a.v1");
        await Assert.ThrowsAsync<AssociationDecisionValidationException>(() =>
            service.DecideAsync(
                Request(),
                Enumerable.Repeat(template, 257).ToArray(),
                default));
    }

    [Fact]
    public async Task RankerCandidateRequestIsCappedAndContainsNoServerOwnedIdentifiersOrText()
    {
        const int candidateCount = 40;
        ApprovedAssociationTemplate[] templates = Enumerable.Range(0, candidateCount)
            .Select(index => Template($"assoc.c{index:D2}.v1"))
            .ToArray();
        ApprovedTextVariantFixture[] fixtures = Enumerable.Range(0, candidateCount)
            .Select(index => Variant($"assoc.c{index:D2}.v1", $"var.c{index:D2}.controlled"))
            .ToArray();
        var ranker = new MockAssociationRanker("{ not-json");

        StoryThreadContract result = await Service(ranker, Variants(fixtures))
            .DecideAsync(Request(), templates, default);

        Assert.True(result.FallbackUsed);
        AssociationRankerRequest request = Assert.Single(ranker.Requests);
        Assert.Equal(AssociationRankerWireProtocol.MaximumProposalCount, request.Candidates.Count);
        string serialized = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("assoc.c", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("npc.known", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("node.next", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clue.known", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("[approved", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAssemblyShipsOnlyMockAssociationRankerAndNoPublicContentFactory()
    {
        Type interfaceType = typeof(IAssociationRanker);
        Type[] implementations = typeof(AssociationDecisionService).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                interfaceType.IsAssignableFrom(type))
            .ToArray();

        Assert.Equal(new[] { typeof(MockAssociationRanker) }, implementations);
        Assert.Empty(typeof(ApprovedTextVariantRegistry).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(ApprovedFallbackStoryThreadCatalog).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(AssociationRuntimePolicy).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.False(AssociationRuntimePolicy.ReuseOnly.AllowRuntimeGeneration);
    }

    private static AssociationDecisionService Service(
        IAssociationRanker ranker,
        ApprovedTextVariantRegistry variants,
        ApprovedFallbackStoryThreadCatalog? fallbacks = null,
        TimeProvider? timeProvider = null,
        IAssociationDecisionIdFactory? idFactory = null) =>
        new(
            new AssociationRuleFilter(),
            ranker,
            variants,
            fallbacks ?? Fallbacks(Array.Empty<StoryThreadEffectContract>(), new[] { "cue.approved.fallback" }),
            idFactory ?? new SequenceAssociationDecisionIdFactory("thr.security", "genjob.security"),
            timeProvider ?? TimeProvider.System);

    private static AssociationDecisionRequest Request() => new(
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
                    ParseJson("{}")),
                FixedNow,
                1),
        },
        Array.Empty<AssociationTriggerHistory>(),
        "assoc.fallback.default.v1");

    private static AssociationDecisionRequest RequestWithLedgerViolation(string violation)
    {
        AssociationDecisionRequest request = Request();
        NarrativeLedgerEntry entry = request.LedgerEntries[0];
        LedgerEventData source = entry.Event;
        LedgerEventData invalid = violation switch
        {
            "actors-empty" => source with { Actors = Array.Empty<string>() },
            "actors-nine" => source with
            {
                Actors = Enumerable.Range(0, 9).Select(index => $"npc.actor_{index}").ToArray(),
            },
            "actors-duplicate" => source with { Actors = new[] { "npc.same", "npc.same" } },
            "schema-version" => source with { SchemaVersion = "2.0.0" },
            "entry-id" => source with { EntryId = "led.Invalid" },
            "player" => source with { PlayerId = "p.other" },
            "event-type" => source with { Type = "unknown_event" },
            "severity" => source with { Severity = 0 },
            "chapter" => source with { Chapter = "chapter.other" },
            "world-clock" => source with { WorldClock = (long)int.MaxValue + 1 },
            "source-node" => source with { SourceNodeId = "not a node" },
            "fact-prefix" => source with { FactRefs = new[] { "branch.not_a_fact" } },
            "facts-seventeen" => source with
            {
                FactRefs = Enumerable.Range(0, 17).Select(index => $"fact.{index}").ToArray(),
            },
            "facts-duplicate" => source with { FactRefs = new[] { "fact.same", "fact.same" } },
            "payload" => source with { Payload = ParseJson("{\"text\":\"forbidden\"}") },
            "ingest-sequence" => source,
            _ => throw new InvalidOperationException(violation),
        };
        NarrativeLedgerEntry invalidEntry = entry with
        {
            Event = invalid,
            IngestSequence = violation == "ingest-sequence" ? 0 : entry.IngestSequence,
        };
        return request with { LedgerEntries = new[] { invalidEntry } };
    }

    private static ApprovedAssociationTemplate Template(
        string templateId,
        AssociationAllowedEffectContract? effect = null,
        IReadOnlyList<string>? chapterWindow = null,
        string fallbackThreadId = "assoc.fallback.default.v1") =>
        new(
            "security-template.json",
            new AssociationTemplateContract(
                "1.0.0",
                templateId,
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
                    Array.Empty<string>(),
                    chapterWindow ?? new[] { "chapter.red_mist" }),
                new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal)
                {
                    ["target"] = new("ledger.actors[1]"),
                },
                new[] { StoryThreadInjectionPoints.NpcMention },
                "llm_variant_within_style_guide",
                effect is null
                    ? new[]
                    {
                        new AssociationAllowedEffectContract(
                            AssociationEffectOperations.Clue,
                            null,
                            null,
                            "clue.known"),
                    }
                    : new[] { effect },
                AssociationMediaPolicies.ReuseOnly,
                fallbackThreadId,
                0,
                1,
                "approved"));

    private static ApprovedTextVariantFixture Variant(string templateId, string token) =>
        new(
            templateId,
            token,
            StoryThreadInjectionPoints.NpcMention,
            "[approved-test-variant]",
            Array.Empty<string>());

    private static ApprovedTextVariantRegistry Variants(params ApprovedTextVariantFixture[] fixtures) =>
        ApprovedTextVariantRegistry.CreateForTests(fixtures);

    private static ApprovedFallbackStoryThreadCatalog Fallbacks(
        IReadOnlyList<StoryThreadEffectContract> effects,
        IReadOnlyList<string> mediaRefs) =>
        ApprovedFallbackStoryThreadCatalog.CreateForTests(new[]
        {
            new ApprovedFallbackStoryThreadFixture(
                "assoc.fallback.default.v1",
                StoryThreadInjectionPoints.NpcMention,
                "[approved-test-fallback]",
                effects,
                mediaRefs),
        });

    private static string Response(params string[] proposals) =>
        "{\"schemaVersion\":\"1.0.0\",\"proposals\":[" +
        string.Join(',', proposals) + "]}";

    private static string Proposal(
        string candidateToken,
        string variantToken,
        string selections = "[]",
        double score = 1) =>
        $"{{\"candidateToken\":\"{candidateToken}\",\"score\":{score.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"variantToken\":\"{variantToken}\",\"effectSelections\":{selections}}}";

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class DelegateRanker(
        Func<AssociationRankerRequest, CancellationToken, Task<string?>> handler) : IAssociationRanker
    {
        public Task<string?> RankAsync(
            AssociationRankerRequest request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class PendingRanker : IAssociationRanker
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public CancellationToken ProviderCancellation { get; private set; }

        public Task<string?> RankAsync(
            AssociationRankerRequest request,
            CancellationToken cancellationToken)
        {
            ProviderCancellation = cancellationToken;
            _called.TrySetResult();
            return _response.Task;
        }

        public void Complete(string response) => _response.TrySetResult(response);
    }

    private sealed class DelayedRanker(
        TimeProvider timeProvider,
        TimeSpan delay,
        string response) : IAssociationRanker
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public CancellationToken ProviderCancellation { get; private set; }

        public async Task<string?> RankAsync(
            AssociationRankerRequest request,
            CancellationToken cancellationToken)
        {
            ProviderCancellation = cancellationToken;
            _called.TrySetResult();
            await Task.Delay(delay, timeProvider, cancellationToken);
            return response;
        }
    }

    private sealed class CountingIdFactory : IAssociationDecisionIdFactory
    {
        public int ThreadIdCount { get; private set; }

        public int AuditIdCount { get; private set; }

        public string CreateThreadId() => "thr.counting." + ++ThreadIdCount;

        public string CreateAuditId() => "genjob.counting." + ++AuditIdCount;
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync) return _utcNow;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_sync) return _utcNow.UtcTicks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_sync) _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
            List<(TimerCallback Callback, object? State)> callbacks = new();
            lock (_sync)
            {
                _utcNow += amount;
                foreach (ManualTimer timer in _timers.ToArray())
                {
                    if (timer.TryTakeDue(_utcNow, out TimerCallback? callback, out object? state))
                    {
                        callbacks.Add((callback!, state));
                    }
                }
            }

            foreach ((TimerCallback callback, object? state) in callbacks) callback(state);
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period;
            private DateTimeOffset? _dueAt;
            private bool _disposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._sync)
                {
                    if (_disposed) return false;
                    _period = period;
                    _dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : _owner._utcNow + dueTime;
                    return true;
                }
            }

            public bool TryTakeDue(
                DateTimeOffset now,
                out TimerCallback? callback,
                out object? state)
            {
                callback = null;
                state = null;
                if (_disposed || _dueAt is null || _dueAt > now) return false;
                callback = _callback;
                state = _state;
                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
                return true;
            }

            public void Dispose()
            {
                lock (_owner._sync)
                {
                    _disposed = true;
                    _dueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
