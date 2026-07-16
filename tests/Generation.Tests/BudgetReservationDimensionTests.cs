using Lingmai.RedMist.Generation.Budget;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetReservationDimensionTests
{
    public static TheoryData<BudgetScopeKind> EveryScope()
    {
        var data = new TheoryData<BudgetScopeKind>();
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds) data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryScope))]
    public async Task AnyInsufficientDimensionFailsTheWholeReservationWithoutPartialDebits(
        BudgetScopeKind insufficientKind)
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(
            limitMicros: 100,
            constrained: insufficientKind,
            constrainedLimitMicros: 9);

        BudgetReservationDecision decision = await repository.ReserveAsync(
            BudgetTestData.Request(maximumCostMicros: 10),
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.FallbackRequired, decision.Outcome);
        Assert.Null(decision.ReservationId);
        Assert.Equal("budget.limit_exceeded", decision.ReasonCode);
        Assert.Equal(insufficientKind, decision.ExceededScopeKind);
        await AssertAllReservedAsync(repository, expectedMicros: 0);
    }

    [Fact]
    public async Task ExactBoundaryReservesAgainstAllFiveDimensions()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(10);

        BudgetReservationDecision decision = await repository.ReserveAsync(
            BudgetTestData.Request(maximumCostMicros: 10),
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.Reserved, decision.Outcome);
        Assert.NotNull(decision.ReservationId);
        Assert.Null(decision.ReasonCode);
        Assert.Null(decision.ExceededScopeKind);
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds)
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(new Money(10, "CNY"), bucket.Reserved);
            Assert.Equal(Money.Zero("CNY"), bucket.Spent);
            Assert.Equal(Money.Zero("CNY"), bucket.Available);
        }
    }

    [Theory]
    [MemberData(nameof(EveryScope))]
    public async Task MissingLimitFailsClosedAndDoesNotTouchConfiguredBuckets(
        BudgetScopeKind missingKind)
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(
            limitMicros: 100,
            omitted: missingKind);

        BudgetReservationDecision decision = await repository.ReserveAsync(
            BudgetTestData.Request(),
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.FallbackRequired, decision.Outcome);
        Assert.Equal("budget.limit_missing", decision.ReasonCode);
        Assert.Equal(missingKind, decision.ExceededScopeKind);
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds.Where(kind => kind != missingKind))
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(0, bucket.Reserved.Micros);
        }
    }

    [Fact]
    public async Task CurrencyMismatchFailsClosedWithoutPartialDebits()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(
            limitMicros: 100,
            currency: "USD");

        BudgetReservationDecision decision = await repository.ReserveAsync(
            BudgetTestData.Request(currency: "CNY"),
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.FallbackRequired, decision.Outcome);
        Assert.Equal("budget.currency_mismatch", decision.ReasonCode);
        await AssertAllReservedAsync(repository, expectedMicros: 0, currency: "USD");
    }

    private static async Task AssertAllReservedAsync(
        InMemoryBudgetRepository repository,
        long expectedMicros,
        string currency = "CNY")
    {
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds)
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(new Money(expectedMicros, currency), bucket.Reserved);
        }
    }
}
