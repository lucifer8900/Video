namespace Lingmai.RedMist.Generation.Budget;

public sealed record Money
{
    private static readonly HashSet<string> SupportedCurrencies =
        ["CNY", "USD", "EUR", "GBP", "JPY", "HKD"];

    public Money(long micros, string currency)
    {
        if (micros < 0) throw new ArgumentOutOfRangeException(nameof(micros));
        if (currency is null || !SupportedCurrencies.Contains(currency))
            throw new ArgumentException("A supported uppercase ISO 4217 currency is required.", nameof(currency));
        Micros = micros;
        Currency = currency;
    }

    public long Micros { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);

    public static Money Add(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.Micros + right.Micros), left.Currency);
    }

    public static Money Subtract(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        if (right.Micros > left.Micros) throw new ArgumentOutOfRangeException(nameof(right));
        return new Money(left.Micros - right.Micros, left.Currency);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
            throw new CurrencyMismatchException();
    }
}

public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException()
        : base("Budget currencies do not match.")
    {
    }
}

public enum BudgetScopeKind
{
    Account,
    Device,
    Chapter,
    Daily,
    Project,
}

public record BudgetScopeContext
{
    public BudgetScopeContext(string accountId, string deviceId, string chapterId, string projectId)
    {
        AccountId = RequireKey(accountId, nameof(accountId));
        DeviceId = RequireKey(deviceId, nameof(deviceId));
        ChapterId = RequireKey(chapterId, nameof(chapterId));
        ProjectId = RequireKey(projectId, nameof(projectId));
    }

    public string AccountId { get; }

    public string DeviceId { get; }

    public string ChapterId { get; }

    public string ProjectId { get; }

    public override string ToString() => "BudgetScopeContext(redacted)";

    private static string RequireKey(string? value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A server-resolved budget key is required.", parameterName);
}

public sealed record BudgetContext : BudgetScopeContext
{
    public BudgetContext(
        string accountId,
        string deviceId,
        string chapterId,
        string projectId,
        DateOnly utcBudgetDay)
        : base(accountId, deviceId, chapterId, projectId) => UtcBudgetDay = utcBudgetDay;

    public DateOnly UtcBudgetDay { get; }

    public override string ToString() => "BudgetContext(redacted)";
}

public sealed record BudgetScope(BudgetScopeKind Kind, string Key)
{
    public static BudgetScope Resolve(BudgetScopeKind kind, BudgetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return kind switch
        {
            BudgetScopeKind.Account => new BudgetScope(kind, $"account:{context.AccountId}"),
            BudgetScopeKind.Device => new BudgetScope(kind, $"device:{context.DeviceId}"),
            BudgetScopeKind.Chapter => new BudgetScope(kind, $"chapter:{context.ChapterId}"),
            BudgetScopeKind.Daily => new BudgetScope(
                kind,
                $"daily:{context.ProjectId}:{context.UtcBudgetDay:yyyy-MM-dd}"),
            BudgetScopeKind.Project => new BudgetScope(kind, $"project:{context.ProjectId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public override string ToString() => $"BudgetScope(Kind={Kind}, Key=redacted)";
}

public sealed class BudgetLimitPolicy
{
    public long AccountLimitMicros { get; set; }

    public long DeviceLimitMicros { get; set; }

    public long ChapterLimitMicros { get; set; }

    public long DailyLimitMicros { get; set; }

    public long ProjectLimitMicros { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string DailyTimeZoneId { get; set; } = "UTC";

    public long LimitFor(BudgetScopeKind kind) => kind switch
    {
        BudgetScopeKind.Account => AccountLimitMicros,
        BudgetScopeKind.Device => DeviceLimitMicros,
        BudgetScopeKind.Chapter => ChapterLimitMicros,
        BudgetScopeKind.Daily => DailyLimitMicros,
        BudgetScopeKind.Project => ProjectLimitMicros,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public sealed record BudgetReservationRequest
{
    public BudgetReservationRequest()
    {
    }

    public BudgetReservationRequest(
        string idempotencyKey,
        string requestFingerprint,
        BudgetContext context,
        Money maximumCost,
        string priceVersion,
        string providerKey,
        string modelSnapshot)
    {
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        Context = context;
        EstimatedCostMicros = maximumCost.Micros;
        MaximumCostMicros = maximumCost.Micros;
        Currency = maximumCost.Currency;
        PriceVersion = priceVersion;
        ProviderKey = providerKey;
        ModelSnapshot = modelSnapshot;
        RequestedAtUtc = new DateTimeOffset(
            context.UtcBudgetDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint { get; init; } = string.Empty;

    public BudgetScopeContext Context { get; init; } = null!;

    public long EstimatedCostMicros { get; init; }

    public long MaximumCostMicros { get; init; }

    public string Currency { get; init; } = string.Empty;

    public string PriceVersion { get; init; } = string.Empty;

    public string ProviderKey { get; init; } = string.Empty;

    public string ModelSnapshot { get; init; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; init; }

    public override string ToString() =>
        $"BudgetReservationRequest(Currency={Currency}, EstimatedMicros={EstimatedCostMicros}, MaximumMicros={MaximumCostMicros})";
}

public enum BudgetReservationStatus
{
    Reserved,
    Dispatching,
    ReconciliationPending,
    Settled,
    Released,
    Denied,
}

public sealed record BudgetReservation(
    Guid Id,
    BudgetReservationStatus Status,
    long EstimatedCostMicros,
    long MaximumCostMicros,
    long? ActualCostMicros,
    bool EstimatorExceeded,
    bool CircuitOpened,
    string PriceVersion,
    string ProviderKey,
    string ModelSnapshot);

public sealed record BudgetReserveDecision(
    bool Admitted,
    BudgetReservation? Reservation,
    BudgetScopeKind? DeniedScope,
    bool Replayed,
    string? ReasonCode = null);

public enum BudgetReservationOutcome
{
    Reserved,
    FallbackRequired,
}

public sealed record BudgetReservationDecision(
    BudgetReservationOutcome Outcome,
    Guid? ReservationId,
    string? ReasonCode,
    BudgetScopeKind? ExceededScopeKind)
{
    public static implicit operator BudgetReservationDecision(BudgetReserveDecision source) =>
        new(
            source.Admitted ? BudgetReservationOutcome.Reserved : BudgetReservationOutcome.FallbackRequired,
            source.Reservation?.Id,
            source.ReasonCode,
            source.DeniedScope);
}

public sealed record BudgetCounters(
    long ReservedMicros,
    long ConsumedMicros,
    bool CircuitOpen);

public sealed record BudgetBucketSnapshot(Money Limit, Money Reserved, Money Spent)
{
    public Money Available => Money.Subtract(Money.Subtract(Limit, Reserved), Spent);
}

public interface IBudgetRepository
{
    Task ConfigureLimitsAsync(
        BudgetScopeContext context,
        BudgetLimitPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<BudgetReserveDecision> ReserveAsync(
        BudgetReservationRequest request,
        CancellationToken cancellationToken);

    Task<bool> TryClaimDispatchAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<BudgetReservation> SettleAsync(
        Guid reservationId,
        string eventId,
        long actualCostMicros,
        CancellationToken cancellationToken);

    Task<BudgetReservation> ReleaseAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken);

    Task<BudgetReservation> MarkReconciliationPendingAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>> GetCountersAsync(
        BudgetScopeContext context,
        DateTimeOffset atUtc,
        string currency,
        CancellationToken cancellationToken);
}

public enum BudgetDispatchDisposition
{
    Dispatched,
    Fallback,
}

public sealed record BudgetProviderResult<T>(
    T Value,
    long ActualCostMicros,
    string SettlementEventId);

public sealed class BudgetDispatchResult<T>
{
    public BudgetDispatchResult(
        BudgetDispatchDisposition disposition,
        T? value,
        string? fallbackReason,
        string? fallbackMediaRef)
    {
        Disposition = disposition;
        Value = value;
        FallbackReason = fallbackReason;
        FallbackMediaRef = fallbackMediaRef;
    }

    public BudgetDispatchDisposition Disposition { get; }

    public T? Value { get; }

    public string? FallbackReason { get; }

    public string? FallbackMediaRef { get; }

    public override string ToString() =>
        $"BudgetDispatchResult(Disposition={Disposition}, FallbackReason={FallbackReason ?? "none"})";
}

public sealed class BudgetIdempotencyConflictException : InvalidOperationException
{
    public BudgetIdempotencyConflictException(string idempotencyKey)
        : base("The budget idempotency key was reused for a different request.") =>
        IdempotencyKey = idempotencyKey;

    public string IdempotencyKey { get; }
}

public sealed class BudgetEventConflictException : InvalidOperationException
{
    public BudgetEventConflictException()
        : base("The budget event was replayed with a conflicting payload.")
    {
    }

    public string Code => "budget.event_conflict";
}
