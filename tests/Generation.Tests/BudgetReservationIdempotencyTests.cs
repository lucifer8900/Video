using Lingmai.RedMist.Generation.Budget;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetReservationIdempotencyTests
{
    [Fact]
    public async Task SameKeyAndFingerprintReuseOneSuccessfulReservation()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(100);
        BudgetReservationRequest request = BudgetTestData.Request();

        BudgetReservationDecision first = await repository.ReserveAsync(
            request,
            CancellationToken.None);
        BudgetReservationDecision replay = await repository.ReserveAsync(
            request,
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.Reserved, first.Outcome);
        Assert.Equal(BudgetReservationOutcome.Reserved, replay.Outcome);
        Assert.NotNull(first.ReservationId);
        Assert.Equal(first.ReservationId, replay.ReservationId);
        await AssertAllReservedOnceAsync(repository);
    }

    [Fact]
    public async Task SameKeyWithDifferentFingerprintIsAConflictAndDoesNotDebitAgain()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(100);
        BudgetReservationRequest original = BudgetTestData.Request(
            idempotencyKey: "shared-budget-key",
            fingerprint: BudgetTestData.Fingerprint(1));
        BudgetReservationRequest conflicting = BudgetTestData.Request(
            idempotencyKey: "shared-budget-key",
            fingerprint: BudgetTestData.Fingerprint(2));

        await repository.ReserveAsync(original, CancellationToken.None);
        BudgetIdempotencyConflictException exception = await Assert.ThrowsAsync<BudgetIdempotencyConflictException>(
            () => repository.ReserveAsync(conflicting, CancellationToken.None));

        Assert.Equal("shared-budget-key", exception.IdempotencyKey);
        await AssertAllReservedOnceAsync(repository);
    }

    [Fact]
    public async Task SameKeyAndFingerprintWithDifferentMaximumIsAConflict()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(100);
        BudgetReservationRequest original = BudgetTestData.Request(
            idempotencyKey: "shared-budget-payload",
            fingerprint: BudgetTestData.Fingerprint(7),
            maximumCostMicros: 10);
        BudgetReservationRequest conflicting = BudgetTestData.Request(
            idempotencyKey: "shared-budget-payload",
            fingerprint: BudgetTestData.Fingerprint(7),
            maximumCostMicros: 11);

        await repository.ReserveAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<BudgetIdempotencyConflictException>(
            () => repository.ReserveAsync(conflicting, CancellationToken.None));
        await AssertAllReservedOnceAsync(repository);
    }

    [Fact]
    public async Task SameKeyAndFingerprintWithDifferentPriceVersionIsAConflict()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(100);
        BudgetReservationRequest original = BudgetTestData.Request(
            idempotencyKey: "shared-budget-price",
            fingerprint: BudgetTestData.Fingerprint(8));
        BudgetReservationRequest conflicting = original with
        {
            PriceVersion = "test.price.v2",
        };

        await repository.ReserveAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<BudgetIdempotencyConflictException>(
            () => repository.ReserveAsync(conflicting, CancellationToken.None));
        await AssertAllReservedOnceAsync(repository);
    }

    [Fact]
    public async Task SameKeyAndFingerprintReuseFailClosedDecisionAfterLimitChanges()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(
            limitMicros: 100,
            constrained: BudgetScopeKind.Chapter,
            constrainedLimitMicros: 9);
        BudgetReservationRequest request = BudgetTestData.Request(maximumCostMicros: 10);

        BudgetReservationDecision first = await repository.ReserveAsync(
            request,
            CancellationToken.None);
        repository.ConfigureLimit(
            BudgetScope.Resolve(BudgetScopeKind.Chapter, BudgetTestData.Context),
            new Money(100, "CNY"));
        BudgetReservationDecision replay = await repository.ReserveAsync(
            request,
            CancellationToken.None);

        Assert.Equal(BudgetReservationOutcome.FallbackRequired, first.Outcome);
        Assert.Equal(BudgetReservationOutcome.FallbackRequired, replay.Outcome);
        Assert.Equal("budget.limit_exceeded", replay.ReasonCode);
        Assert.Equal(BudgetScopeKind.Chapter, replay.ExceededScopeKind);
        Assert.Null(replay.ReservationId);
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds)
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(Money.Zero("CNY"), bucket.Reserved);
        }
    }

    private static async Task AssertAllReservedOnceAsync(InMemoryBudgetRepository repository)
    {
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds)
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(new Money(10, "CNY"), bucket.Reserved);
        }
    }
}
