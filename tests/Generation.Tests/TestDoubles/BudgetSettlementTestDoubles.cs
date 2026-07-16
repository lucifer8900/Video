using Lingmai.RedMist.Generation.Budget;

namespace Lingmai.RedMist.Generation.Tests.TestDoubles;

internal static class SettlementBudgetTestData
{
    public static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    public static BudgetScopeContext Context(
        string suffix = "primary") =>
        new(
            $"account-{suffix}",
            $"device-{suffix}",
            $"chapter-{suffix}",
            "project-redmist");

    public static BudgetLimitPolicy UniformPolicy(long limitMicros) =>
        new()
        {
            AccountLimitMicros = limitMicros,
            DeviceLimitMicros = limitMicros,
            ChapterLimitMicros = limitMicros,
            DailyLimitMicros = limitMicros,
            ProjectLimitMicros = limitMicros,
            Currency = "USD",
            DailyTimeZoneId = "Asia/Shanghai",
        };

    public static BudgetReservationRequest Request(
        string idempotencyKey,
        long estimatedMicros,
        long maximumMicros,
        BudgetScopeContext? context = null) =>
        new()
        {
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = $"sha256:{new string('a', 64)}",
            Context = context ?? Context(),
            EstimatedCostMicros = estimatedMicros,
            MaximumCostMicros = maximumMicros,
            Currency = "USD",
            PriceVersion = "test.price.v1",
            ProviderKey = "test.provider.video",
            ModelSnapshot = "test.model.snapshot.v1",
            RequestedAtUtc = FixedNow,
        };
}

internal sealed class FixedBudgetTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class ThrowingBudgetRepository(Exception failure) : IBudgetRepository
{
    public Task ConfigureLimitsAsync(
        BudgetScopeContext context,
        BudgetLimitPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) => Task.FromException(failure);

    public Task<BudgetReserveDecision> ReserveAsync(
        BudgetReservationRequest request,
        CancellationToken cancellationToken) => Task.FromException<BudgetReserveDecision>(failure);

    public Task<bool> TryClaimDispatchAsync(
        Guid reservationId,
        CancellationToken cancellationToken) => Task.FromException<bool>(failure);

    public Task<BudgetReservation> SettleAsync(
        Guid reservationId,
        string eventId,
        long actualCostMicros,
        CancellationToken cancellationToken) => Task.FromException<BudgetReservation>(failure);

    public Task<BudgetReservation> ReleaseAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) => Task.FromException<BudgetReservation>(failure);

    public Task<BudgetReservation> MarkReconciliationPendingAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) => Task.FromException<BudgetReservation>(failure);

    public Task<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>> GetCountersAsync(
        BudgetScopeContext context,
        DateTimeOffset atUtc,
        string currency,
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>>(failure);
}

internal sealed class MissingReservationBudgetRepository : IBudgetRepository
{
    public Task ConfigureLimitsAsync(
        BudgetScopeContext context,
        BudgetLimitPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<BudgetReserveDecision> ReserveAsync(
        BudgetReservationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BudgetReserveDecision(
            Admitted: true,
            Reservation: null,
            DeniedScope: null,
            Replayed: false));

    public Task<bool> TryClaimDispatchAsync(
        Guid reservationId,
        CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<BudgetReservation> SettleAsync(
        Guid reservationId,
        string eventId,
        long actualCostMicros,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BudgetReservation> ReleaseAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BudgetReservation> MarkReconciliationPendingAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>> GetCountersAsync(
        BudgetScopeContext context,
        DateTimeOffset atUtc,
        string currency,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class CountingBudgetProvider
{
    private readonly TimeSpan _delay;
    private int _callCount;

    public CountingBudgetProvider(TimeSpan? delay = null) =>
        _delay = delay ?? TimeSpan.Zero;

    public int CallCount => Volatile.Read(ref _callCount);

    public async Task<BudgetProviderResult<string>> InvokeAsync(
        BudgetReservation reservation,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken);
        return new BudgetProviderResult<string>(
            "provider-operation-001",
            ActualCostMicros: reservation.EstimatedCostMicros,
            SettlementEventId: "settlement-provider-operation-001");
    }
}
