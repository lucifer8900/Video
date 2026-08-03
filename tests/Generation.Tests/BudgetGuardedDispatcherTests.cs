using Lingmai.RedMist.Generation.Budget;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetGuardedDispatcherTests
{
    private const string FallbackMediaRef = "media.fallback.budget.v1";

    [Fact]
    public async Task ExceededBudgetReturnsFallbackWithoutCallingProvider()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(99),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        var provider = new CountingBudgetProvider();
        var dispatcher = new BudgetGuardedDispatcher(repository, FallbackMediaRef);

        BudgetDispatchResult<string> result = await dispatcher.DispatchAsync(
            SettlementBudgetTestData.Request("guard-denied", 80, 100),
            provider.InvokeAsync,
            CancellationToken.None);

        Assert.Equal(BudgetDispatchDisposition.Fallback, result.Disposition);
        Assert.Equal("budget.exceeded", result.FallbackReason);
        Assert.Null(result.Value);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task RepositoryFailureFailsClosedWithoutCallingProvider()
    {
        var provider = new CountingBudgetProvider();
        var dispatcher = new BudgetGuardedDispatcher(
            new ThrowingBudgetRepository(
                new InvalidOperationException("database details must remain private")),
            FallbackMediaRef);

        BudgetDispatchResult<string> result = await dispatcher.DispatchAsync(
            SettlementBudgetTestData.Request("guard-storage-failure", 80, 100),
            provider.InvokeAsync,
            CancellationToken.None);

        Assert.Equal(BudgetDispatchDisposition.Fallback, result.Disposition);
        Assert.Equal("budget.unavailable", result.FallbackReason);
        Assert.Equal(0, provider.CallCount);
        Assert.DoesNotContain("database details", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdmittedDecisionWithoutReservationFailsClosed()
    {
        var provider = new CountingBudgetProvider();
        var dispatcher = new BudgetGuardedDispatcher(
            new MissingReservationBudgetRepository(),
            FallbackMediaRef);

        BudgetDispatchResult<string> result = await dispatcher.DispatchAsync(
            SettlementBudgetTestData.Request("guard-missing-reservation", 80, 100),
            provider.InvokeAsync,
            CancellationToken.None);

        Assert.Equal(BudgetDispatchDisposition.Fallback, result.Disposition);
        Assert.Equal("budget.unavailable", result.FallbackReason);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ConcurrentIdempotentReplayDispatchesProviderExactlyOnce()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        var provider = new CountingBudgetProvider(TimeSpan.FromMilliseconds(25));
        var dispatcher = new BudgetGuardedDispatcher(repository, FallbackMediaRef);
        BudgetReservationRequest request =
            SettlementBudgetTestData.Request("guard-32-way-replay", 100, 200);

        Task<BudgetDispatchResult<string>>[] calls = Enumerable.Range(0, 32)
            .Select(_ => dispatcher.DispatchAsync(
                request,
                provider.InvokeAsync,
                CancellationToken.None))
            .ToArray();
        BudgetDispatchResult<string>[] results = await Task.WhenAll(calls);

        Assert.Equal(1, provider.CallCount);
        Assert.All(results, result =>
        {
            Assert.Equal(BudgetDispatchDisposition.Dispatched, result.Disposition);
            Assert.Equal("provider-operation-001", result.Value);
            Assert.Null(result.FallbackReason);
        });
        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(0, value.ReservedMicros);
            Assert.Equal(100, value.ConsumedMicros);
        });
    }

    [Fact]
    public async Task ProviderFailureWithUnknownAcceptanceKeepsReservationForReconciliation()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        var dispatcher = new BudgetGuardedDispatcher(repository, FallbackMediaRef);

        BudgetDispatchResult<string> result = await dispatcher.DispatchAsync<string>(
            SettlementBudgetTestData.Request("guard-provider-unknown", 100, 200),
            (_, _) => throw new IOException("provider response was lost"),
            CancellationToken.None);

        Assert.Equal(BudgetDispatchDisposition.Fallback, result.Disposition);
        Assert.Equal("budget.provider_unavailable", result.FallbackReason);
        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(200, value.ReservedMicros);
            Assert.Equal(0, value.ConsumedMicros);
        });
    }

    [Fact]
    public async Task CancellationAfterProviderDispatchKeepsReservationForReconciliation()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        var dispatcher = new BudgetGuardedDispatcher(repository, FallbackMediaRef);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync<string>(
                SettlementBudgetTestData.Request("guard-provider-cancelled", 100, 200),
                (_, token) =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<BudgetProviderResult<string>>(token);
                },
                cancellation.Token));

        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(200, value.ReservedMicros);
            Assert.Equal(0, value.ConsumedMicros);
        });
    }

    [Fact]
    public async Task BudgetFallbackCarriesTheConfiguredOfflineMediaReference()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(99),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        var dispatcher = new BudgetGuardedDispatcher(repository, FallbackMediaRef);

        BudgetDispatchResult<string> result = await dispatcher.DispatchAsync(
            SettlementBudgetTestData.Request("guard-fallback-media", 80, 100),
            new CountingBudgetProvider().InvokeAsync,
            CancellationToken.None);

        Assert.Equal(BudgetDispatchDisposition.Fallback, result.Disposition);
        Assert.Equal(FallbackMediaRef, result.FallbackMediaRef);
    }
}
