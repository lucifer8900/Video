using Lingmai.RedMist.Generation.Budget;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetReservationConcurrencyTests
{
    [Fact]
    public async Task SixtyFourConcurrentReplaysConvergeOnOneReservation()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(100);
        BudgetReservationRequest request = BudgetTestData.Request(
            idempotencyKey: "concurrent-replay",
            fingerprint: BudgetTestData.Fingerprint(64));

        BudgetReservationDecision[] decisions = await RunConcurrentlyAsync(
            Enumerable.Repeat(request, 64),
            repository);

        Assert.All(
            decisions,
            decision => Assert.Equal(BudgetReservationOutcome.Reserved, decision.Outcome));
        Assert.Single(decisions.Select(decision => decision.ReservationId).Distinct());
        Assert.NotNull(decisions[0].ReservationId);
        await AssertEveryBucketReservedExactlyAsync(repository, expectedMicros: 10);
    }

    [Fact]
    public async Task SixtyFourCompetingRequestsAllowOnlyOneWholeReservation()
    {
        InMemoryBudgetRepository repository = BudgetTestData.RepositoryWithLimits(10);
        BudgetReservationRequest[] requests = Enumerable.Range(1, 64)
            .Select(index => BudgetTestData.Request(
                idempotencyKey: $"competing-budget-{index}",
                fingerprint: BudgetTestData.Fingerprint(index)))
            .ToArray();

        BudgetReservationDecision[] decisions = await RunConcurrentlyAsync(requests, repository);

        Assert.Single(decisions.Where(
            decision => decision.Outcome == BudgetReservationOutcome.Reserved));
        Assert.Equal(
            63,
            decisions.Count(decision =>
                decision.Outcome == BudgetReservationOutcome.FallbackRequired));
        Assert.Single(decisions.Where(decision => decision.ReservationId is not null));
        Assert.All(
            decisions.Where(decision => decision.Outcome == BudgetReservationOutcome.FallbackRequired),
            decision => Assert.Equal("budget.limit_exceeded", decision.ReasonCode));
        await AssertEveryBucketReservedExactlyAsync(repository, expectedMicros: 10);
    }

    private static async Task<BudgetReservationDecision[]> RunConcurrentlyAsync(
        IEnumerable<BudgetReservationRequest> requests,
        InMemoryBudgetRepository repository)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BudgetReservationDecision>[] tasks = requests
            .Select(request => Task.Run(async () =>
            {
                await start.Task;
                return (BudgetReservationDecision)await repository.ReserveAsync(
                    request,
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task AssertEveryBucketReservedExactlyAsync(
        InMemoryBudgetRepository repository,
        long expectedMicros)
    {
        foreach (BudgetScopeKind kind in BudgetTestData.AllScopeKinds)
        {
            BudgetBucketSnapshot bucket = await repository.GetBucketAsync(
                BudgetScope.Resolve(kind, BudgetTestData.Context),
                CancellationToken.None);
            Assert.Equal(new Money(expectedMicros, "CNY"), bucket.Reserved);
        }
    }
}
