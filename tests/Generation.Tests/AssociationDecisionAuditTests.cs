using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationDecisionAuditTests
{
    private const string ValidRankerResponse = """
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
        """;

    [Fact]
    public async Task SuccessfulThreadPersistsCompleteAuditBeforeReturn()
    {
        var audits = new InMemoryAssociationDecisionAuditRepository();
        var metrics = new AssociationDecisionMetrics();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker(ValidRankerResponse),
            audits,
            metrics);

        StoryThreadContract thread = await service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            CancellationToken.None);
        AssociationDecisionAuditContract? audit = await audits.GetAsync(
            thread.AuditRef,
            CancellationToken.None);

        Assert.NotNull(audit);
        Assert.Equal(thread.ThreadId, audit!.ThreadId);
        Assert.Equal(thread.TemplateId, audit.TemplateId);
        Assert.Equal(thread.ResolvedParams, audit.ResolvedParams);
        Assert.StartsWith("sha256:", audit.InputHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", audit.PlayerRefHash, StringComparison.Ordinal);
        Assert.DoesNotContain("p.known", audit.PlayerRefHash, StringComparison.Ordinal);
        AssociationProviderAuditContract provider = Assert.Single(audit.Providers);
        Assert.Equal("ranker", provider.Role);
        Assert.Equal("mock.association-ranker", provider.ProviderKey);
        Assert.Equal("prompt.association-ranker.mock.v1", provider.PromptVersion);
        Assert.Equal("model.association-ranker.mock.v1", provider.ModelSnapshot);
        Assert.True(provider.Invoked);
        Assert.Contains(audit.GuardResults, result =>
            result.Stage == "deterministic" && result.Decision == "passed");
        Assert.Contains(audit.GuardResults, result =>
            result.Stage == "signing" && result.Decision == "passed");
        Assert.False(audit.FallbackUsed);
        Assert.Null(audit.OutcomeReason);
        Assert.Equal(1, audit.CandidateCount);

        string persisted = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("[approved-test-variant]", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("providerResponse", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", persisted, StringComparison.OrdinalIgnoreCase);
        AssociationDecisionMetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.TotalDecisions);
        Assert.Equal(0, snapshot.FallbackDecisions);
    }

    [Fact]
    public async Task InvalidRankerResponseReturnsOneAuditedFallbackWithStableReason()
    {
        var audits = new InMemoryAssociationDecisionAuditRepository();
        var metrics = new AssociationDecisionMetrics();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker("{ not-json"),
            audits,
            metrics);

        StoryThreadContract thread = await service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            CancellationToken.None);
        AssociationDecisionAuditContract? audit = await audits.GetAsync(
            thread.AuditRef,
            CancellationToken.None);

        Assert.True(thread.FallbackUsed);
        Assert.NotNull(audit);
        Assert.True(audit!.FallbackUsed);
        Assert.Equal("ranker_invalid_response", audit.OutcomeReason);
        Assert.Equal(1, audit.CandidateCount);
        Assert.True(Assert.Single(audit.Providers).Invoked);
        Assert.Contains(audit.GuardResults, result =>
            result.Stage == "selection" &&
            result.Decision == "rejected" &&
            result.ReasonCode == "ranker_invalid_response");

        AssociationDecisionMetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.TotalDecisions);
        Assert.Equal(1, snapshot.FallbackDecisions);
        Assert.Equal(1d, snapshot.FallbackRate);
        Assert.Equal(1, snapshot.CandidateCountDistribution[1]);
    }

    [Fact]
    public async Task NoCandidatesStillAuditsConfiguredRankerAsNotInvoked()
    {
        var audits = new InMemoryAssociationDecisionAuditRepository();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker(ValidRankerResponse),
            audits,
            new AssociationDecisionMetrics());

        StoryThreadContract thread = await service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            Array.Empty<ApprovedAssociationTemplate>(),
            CancellationToken.None);
        AssociationDecisionAuditContract? audit = await audits.GetAsync(
            thread.AuditRef,
            CancellationToken.None);

        Assert.NotNull(audit);
        Assert.True(thread.FallbackUsed);
        Assert.Equal("no_candidates", audit!.OutcomeReason);
        Assert.Equal(0, audit.CandidateCount);
        Assert.False(Assert.Single(audit.Providers).Invoked);
    }

    [Fact]
    public async Task NullRankerResultIsAuditedAsUnavailableNotInvalidJson()
    {
        var audits = new InMemoryAssociationDecisionAuditRepository();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker((string?)null),
            audits,
            new AssociationDecisionMetrics());

        StoryThreadContract thread = await service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            CancellationToken.None);
        AssociationDecisionAuditContract? audit = await audits.GetAsync(
            thread.AuditRef,
            CancellationToken.None);

        Assert.NotNull(audit);
        Assert.Equal("ranker_unavailable", audit!.OutcomeReason);
        Assert.Contains(audit.GuardResults, result =>
            result.Stage == "selection" &&
            result.Decision == "rejected" &&
            result.ReasonCode == "ranker_unavailable");
    }

    [Fact]
    public async Task AuditWriteFailureNeverReturnsAnUntraceableThreadOrRecordsSuccessMetrics()
    {
        var metrics = new AssociationDecisionMetrics();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker(ValidRankerResponse),
            new ThrowingAuditRepository(),
            metrics);

        await Assert.ThrowsAsync<AssociationDecisionAuditException>(() => service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            CancellationToken.None));

        AssociationDecisionMetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(0, snapshot.TotalDecisions);
        Assert.Equal(0, snapshot.FallbackDecisions);
    }

    [Fact]
    public async Task CancellationAfterAuditPersistenceStartsCompletesOneAuditedDecision()
    {
        var audits = new BlockingAuditRepository();
        using var cancellation = new CancellationTokenSource();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker(ValidRankerResponse),
            audits,
            new AssociationDecisionMetrics());

        Task<StoryThreadContract> pending = service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            cancellation.Token);
        await audits.Called;
        cancellation.Cancel();
        audits.Complete();

        StoryThreadContract thread = await pending;
        Assert.Equal(thread.AuditRef, audits.Stored?.AuditRef);
    }

    [Fact]
    public async Task MetricsIncludeAuditPersistenceLatencyAndStoreGetsDeadlineToken()
    {
        var time = new ManualTimeProvider();
        var audits = new AdvancingAuditRepository(time, TimeSpan.FromMilliseconds(250));
        var metrics = new AssociationDecisionMetrics();
        AssociationDecisionService service = AssociationDecisionServiceTests.CreateService(
            new MockAssociationRanker(ValidRankerResponse),
            audits,
            metrics,
            time);

        await service.DecideAsync(
            AssociationDecisionServiceTests.Request(),
            new[] { AssociationDecisionServiceTests.ApprovedTemplate() },
            CancellationToken.None);

        Assert.True(audits.StoreTokenCanBeCanceled);
        Assert.True(metrics.GetSnapshot().LatencyP50Milliseconds >= 250d);
    }

    [Fact]
    public void RankerAuditProfileCannotMasqueradeAsReviewer()
    {
        Assert.Throws<ArgumentException>(() =>
            AssociationDecisionServiceTests.CreateService(new WrongRoleRanker()));
    }

    private sealed class ThrowingAuditRepository : IAssociationDecisionAuditRepository
    {
        public ValueTask StoreAsync(
            AssociationDecisionAuditContract audit,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("audit store unavailable"));

        public ValueTask<AssociationDecisionAuditContract?> GetAsync(
            string auditRef,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AssociationDecisionAuditContract?>(null);

        public ValueTask<int> DeletePlayerAsync(
            string playerId,
            CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private sealed class BlockingAuditRepository : IAssociationDecisionAuditRepository
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public AssociationDecisionAuditContract? Stored { get; private set; }

        public async ValueTask StoreAsync(
            AssociationDecisionAuditContract audit,
            CancellationToken cancellationToken)
        {
            Stored = audit;
            _called.TrySetResult();
            await _release.Task;
        }

        public ValueTask<AssociationDecisionAuditContract?> GetAsync(
            string auditRef,
            CancellationToken cancellationToken) => ValueTask.FromResult(Stored);

        public ValueTask<int> DeletePlayerAsync(
            string playerId,
            CancellationToken cancellationToken) => ValueTask.FromResult(0);

        public void Complete() => _release.TrySetResult();
    }

    private sealed class AdvancingAuditRepository(
        ManualTimeProvider timeProvider,
        TimeSpan elapsed) : IAssociationDecisionAuditRepository
    {
        public bool StoreTokenCanBeCanceled { get; private set; }

        public ValueTask StoreAsync(
            AssociationDecisionAuditContract audit,
            CancellationToken cancellationToken)
        {
            StoreTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            timeProvider.Advance(elapsed);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AssociationDecisionAuditContract?> GetAsync(
            string auditRef,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AssociationDecisionAuditContract?>(null);

        public ValueTask<int> DeletePlayerAsync(
            string playerId,
            CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private sealed class WrongRoleRanker : IAssociationRanker
    {
        public AssociationProviderAuditProfile AuditProfile { get; } = new(
            "reviewer",
            "test.wrong-role",
            "prompt.test.wrong-role.v1",
            "model.test.wrong-role.v1");

        public Task<string?> RankAsync(
            AssociationRankerRequest request,
            CancellationToken cancellationToken) => Task.FromResult<string?>(ValidRankerResponse);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero) +
            TimeSpan.FromTicks(Volatile.Read(ref _timestamp));

        public void Advance(TimeSpan amount) =>
            Interlocked.Add(ref _timestamp, amount.Ticks);
    }
}
