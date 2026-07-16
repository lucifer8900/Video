using Lingmai.RedMist.Generation.Budget;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetSettlementTests
{
    [Theory]
    [InlineData(250, false, false)]
    [InlineData(300, false, false)]
    [InlineData(350, true, false)]
    [InlineData(400, true, false)]
    [InlineData(550, true, true)]
    public async Task SettlementRecordsActualAndReleasesReservation(
        long actualCostMicros,
        bool expectedEstimatorExceeded,
        bool expectedCircuitOpened)
    {
        var repository = await CreateRepositoryAsync(limitMicros: 1000);
        BudgetReserveDecision decision = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("settlement-boundaries", 300, 400),
            CancellationToken.None);
        BudgetReservation reserved = Assert.IsType<BudgetReservation>(decision.Reservation);

        BudgetReservation settled = await repository.SettleAsync(
            reserved.Id,
            "event-provider-invoice-001",
            actualCostMicros,
            CancellationToken.None);
        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);

        Assert.Equal(BudgetReservationStatus.Settled, settled.Status);
        Assert.Equal(actualCostMicros, settled.ActualCostMicros);
        Assert.Equal(expectedEstimatorExceeded, settled.EstimatorExceeded);
        Assert.Equal(expectedCircuitOpened, settled.CircuitOpened);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(0, value.ReservedMicros);
            Assert.Equal(actualCostMicros, value.ConsumedMicros);
            Assert.Equal(expectedCircuitOpened, value.CircuitOpen);
        });
    }

    [Fact]
    public async Task ExplicitReleaseReturnsAllReservedBudgetWithoutConsumption()
    {
        var repository = await CreateRepositoryAsync(limitMicros: 1000);
        BudgetReserveDecision decision = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("release-before-provider", 300, 400),
            CancellationToken.None);
        BudgetReservation reserved = Assert.IsType<BudgetReservation>(decision.Reservation);

        BudgetReservation released = await repository.ReleaseAsync(
            reserved.Id,
            "event-provider-not-accepted",
            CancellationToken.None);
        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);

        Assert.Equal(BudgetReservationStatus.Released, released.Status);
        Assert.Equal(0, released.ActualCostMicros);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(0, value.ReservedMicros);
            Assert.Equal(0, value.ConsumedMicros);
        });
    }

    [Fact]
    public async Task UnknownProviderCostRemainsReservedUntilReconciled()
    {
        var repository = await CreateRepositoryAsync(limitMicros: 1000);
        BudgetReserveDecision decision = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("timeout-after-provider-accepted", 300, 400),
            CancellationToken.None);
        BudgetReservation reserved = Assert.IsType<BudgetReservation>(decision.Reservation);

        BudgetReservation pending = await repository.MarkReconciliationPendingAsync(
            reserved.Id,
            "event-provider-timeout-unknown-cost",
            CancellationToken.None);
        IReadOnlyDictionary<BudgetScopeKind, BudgetCounters> counters =
            await repository.GetCountersAsync(
                SettlementBudgetTestData.Context(),
                SettlementBudgetTestData.FixedNow,
                "USD",
                CancellationToken.None);

        Assert.Equal(BudgetReservationStatus.ReconciliationPending, pending.Status);
        Assert.Null(pending.ActualCostMicros);
        Assert.All(counters.Values, value =>
        {
            Assert.Equal(400, value.ReservedMicros);
            Assert.Equal(0, value.ConsumedMicros);
        });

        BudgetReservation reconciled = await repository.SettleAsync(
            reserved.Id,
            "event-provider-invoice-after-timeout",
            275,
            CancellationToken.None);
        Assert.Equal(BudgetReservationStatus.Settled, reconciled.Status);
        Assert.Equal(275, reconciled.ActualCostMicros);
    }

    [Fact]
    public async Task SettlementEventReplayIsIdempotentButConflictingPayloadIsRejected()
    {
        var repository = await CreateRepositoryAsync(limitMicros: 1000);
        BudgetReserveDecision decision = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("settlement-event-idempotency", 300, 400),
            CancellationToken.None);
        BudgetReservation reserved = Assert.IsType<BudgetReservation>(decision.Reservation);

        BudgetReservation first = await repository.SettleAsync(
            reserved.Id,
            "event-stable-invoice",
            250,
            CancellationToken.None);
        BudgetReservation replay = await repository.SettleAsync(
            reserved.Id,
            "event-stable-invoice",
            250,
            CancellationToken.None);

        Assert.Equal(first, replay);
        BudgetEventConflictException conflict = await Assert.ThrowsAsync<BudgetEventConflictException>(
            () => repository.SettleAsync(
                reserved.Id,
                "event-stable-invoice",
                251,
                CancellationToken.None));
        Assert.Equal("budget.event_conflict", conflict.Code);
    }

    private static async Task<InMemoryBudgetRepository> CreateRepositoryAsync(long limitMicros)
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(limitMicros),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        return repository;
    }
}
