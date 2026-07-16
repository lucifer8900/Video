namespace Lingmai.RedMist.Generation.Budget;

/// <summary>
/// Deterministic, process-local budget repository used by tests and local development.
/// Every state transition is protected by one lock so a reservation either updates all
/// five budget dimensions or updates none of them.
/// </summary>
public sealed class InMemoryBudgetRepository : IBudgetRepository
{
    private static readonly BudgetScopeKind[] ScopeKinds = Enum.GetValues<BudgetScopeKind>();

    private readonly object _gate = new();
    private readonly Dictionary<BudgetScope, BucketState> _buckets = [];
    private readonly Dictionary<Guid, ReservationState> _reservations = [];
    private readonly Dictionary<string, IdempotencyEntry> _idempotency =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventEntry> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<ContextKey, TimeZoneInfo> _dailyTimeZones = [];

    public void ConfigureLimit(BudgetScope scope, Money limit)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(limit);
        if (string.IsNullOrWhiteSpace(scope.Key))
            throw new ArgumentException("A budget scope key is required.", nameof(scope));

        lock (_gate)
        {
            ConfigureLimitUnderLock(scope, limit);
        }
    }

    public Task ConfigureLimitsAsync(
        BudgetScopeContext context,
        BudgetLimitPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);

        TimeZoneInfo dailyTimeZone = FindTimeZone(policy.DailyTimeZoneId);
        DateOnly dailyDate = DateAt(nowUtc, dailyTimeZone);
        BudgetScope[] scopes = ResolveScopes(context, dailyDate);
        Money[] limits = ScopeKinds
            .Select(kind => new Money(policy.LimitFor(kind), policy.Currency))
            .ToArray();

        lock (_gate)
        {
            for (int index = 0; index < scopes.Length; index++)
            {
                EnsureLimitCanBeConfigured(scopes[index], limits[index]);
            }

            for (int index = 0; index < scopes.Length; index++)
            {
                ConfigureLimitUnderLock(scopes[index], limits[index]);
            }

            _dailyTimeZones[ContextKey.From(context)] = dailyTimeZone;
        }

        return Task.CompletedTask;
    }

    public Task<BudgetBucketSnapshot> GetBucketAsync(
        BudgetScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);

        lock (_gate)
        {
            BucketState bucket = GetRequiredBucket(scope);
            return Task.FromResult(new BudgetBucketSnapshot(
                bucket.Limit,
                new Money(bucket.ReservedMicros, bucket.Limit.Currency),
                new Money(bucket.ConsumedMicros, bucket.Limit.Currency)));
        }
    }

    public Task<BudgetReserveDecision> ReserveAsync(
        BudgetReservationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        lock (_gate)
        {
            ReservationIdentity identity = CreateReservationIdentityUnderLock(request);
            if (_idempotency.TryGetValue(request.IdempotencyKey, out IdempotencyEntry? prior))
            {
                if (prior.Identity != identity)
                {
                    throw new BudgetIdempotencyConflictException(request.IdempotencyKey);
                }

                return Task.FromResult(prior.Decision with { Replayed = true });
            }

            BudgetScope[] scopes = ResolveRequestScopesUnderLock(request);

            foreach (BudgetScope scope in scopes)
            {
                if (!_buckets.TryGetValue(scope, out BucketState? bucket))
                {
                    return Task.FromResult(CacheDenied(
                        request,
                        identity,
                        scope.Kind,
                        "budget.limit_missing"));
                }

                if (!string.Equals(bucket.Limit.Currency, request.Currency, StringComparison.Ordinal))
                {
                    return Task.FromResult(CacheDenied(
                        request,
                        identity,
                        scope.Kind,
                        "budget.currency_mismatch"));
                }

                if (bucket.CircuitOpen)
                {
                    return Task.FromResult(CacheDenied(
                        request,
                        identity,
                        scope.Kind,
                        "budget.circuit_open"));
                }

                long usedMicros = checked(bucket.ReservedMicros + bucket.ConsumedMicros);
                long availableMicros = bucket.Limit.Micros - usedMicros;
                if (availableMicros < 0 || request.MaximumCostMicros > availableMicros)
                {
                    return Task.FromResult(CacheDenied(
                        request,
                        identity,
                        scope.Kind,
                        "budget.limit_exceeded"));
                }
            }

            foreach (BudgetScope scope in scopes)
            {
                BucketState bucket = _buckets[scope];
                bucket.ReservedMicros = checked(
                    bucket.ReservedMicros + request.MaximumCostMicros);
            }

            var reservation = new BudgetReservation(
                Guid.NewGuid(),
                BudgetReservationStatus.Reserved,
                request.EstimatedCostMicros,
                request.MaximumCostMicros,
                ActualCostMicros: null,
                EstimatorExceeded: false,
                CircuitOpened: false,
                request.PriceVersion,
                request.ProviderKey,
                request.ModelSnapshot);
            _reservations.Add(
                reservation.Id,
                new ReservationState(reservation, scopes, request.Currency));

            var decision = new BudgetReserveDecision(
                Admitted: true,
                Reservation: reservation,
                DeniedScope: null,
                Replayed: false,
                ReasonCode: null);
            _idempotency.Add(
                request.IdempotencyKey,
                new IdempotencyEntry(identity, decision));
            return Task.FromResult(decision);
        }
    }

    public Task<bool> TryClaimDispatchAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_reservations.TryGetValue(reservationId, out ReservationState? state) ||
                state.Current.Status != BudgetReservationStatus.Reserved)
            {
                return Task.FromResult(false);
            }

            state.Current = state.Current with { Status = BudgetReservationStatus.Dispatching };
            return Task.FromResult(true);
        }
    }

    public Task<BudgetReservation> SettleAsync(
        Guid reservationId,
        string eventId,
        long actualCostMicros,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireEventId(eventId);
        if (actualCostMicros < 0)
            throw new ArgumentOutOfRangeException(nameof(actualCostMicros));

        lock (_gate)
        {
            if (TryReplayEvent(
                    eventId,
                    EventKind.Settle,
                    reservationId,
                    actualCostMicros,
                    out BudgetReservation? replay))
            {
                return Task.FromResult(replay);
            }

            ReservationState state = GetRequiredReservation(reservationId);
            EnsureReservationHeld(state);

            foreach (BudgetScope scope in state.Scopes)
            {
                BucketState bucket = GetRequiredBucket(scope);
                EnsureReservationCurrency(bucket, state.Currency);
                if (bucket.ReservedMicros < state.Current.MaximumCostMicros)
                    throw new InvalidOperationException("The reservation counters are inconsistent.");
                _ = checked(bucket.ConsumedMicros + actualCostMicros);
            }

            bool exceededEstimate = actualCostMicros > state.Current.EstimatedCostMicros;
            bool exceededMaximum = actualCostMicros > state.Current.MaximumCostMicros;
            foreach (BudgetScope scope in state.Scopes)
            {
                BucketState bucket = _buckets[scope];
                bucket.ReservedMicros -= state.Current.MaximumCostMicros;
                bucket.ConsumedMicros = checked(bucket.ConsumedMicros + actualCostMicros);
                if (exceededMaximum) bucket.CircuitOpen = true;
            }

            state.HoldsReservation = false;
            state.Current = state.Current with
            {
                Status = BudgetReservationStatus.Settled,
                ActualCostMicros = actualCostMicros,
                EstimatorExceeded = exceededEstimate,
                CircuitOpened = exceededMaximum,
            };
            RecordEvent(eventId, EventKind.Settle, reservationId, actualCostMicros, state.Current);
            return Task.FromResult(state.Current);
        }
    }

    public Task<BudgetReservation> ReleaseAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireEventId(eventId);

        lock (_gate)
        {
            if (TryReplayEvent(
                    eventId,
                    EventKind.Release,
                    reservationId,
                    actualCostMicros: 0,
                    out BudgetReservation? replay))
            {
                return Task.FromResult(replay);
            }

            ReservationState state = GetRequiredReservation(reservationId);
            EnsureReservationHeld(state);
            ReleaseReservedCounters(state);
            state.HoldsReservation = false;
            state.Current = state.Current with
            {
                Status = BudgetReservationStatus.Released,
                ActualCostMicros = 0,
            };
            RecordEvent(eventId, EventKind.Release, reservationId, 0, state.Current);
            return Task.FromResult(state.Current);
        }
    }

    public Task<BudgetReservation> MarkReconciliationPendingAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireEventId(eventId);

        lock (_gate)
        {
            if (TryReplayEvent(
                    eventId,
                    EventKind.ReconciliationPending,
                    reservationId,
                    actualCostMicros: null,
                    out BudgetReservation? replay))
            {
                return Task.FromResult(replay);
            }

            ReservationState state = GetRequiredReservation(reservationId);
            EnsureReservationHeld(state);
            state.Current = state.Current with
            {
                Status = BudgetReservationStatus.ReconciliationPending,
            };
            RecordEvent(
                eventId,
                EventKind.ReconciliationPending,
                reservationId,
                null,
                state.Current);
            return Task.FromResult(state.Current);
        }
    }

    public Task<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>> GetCountersAsync(
        BudgetScopeContext context,
        DateTimeOffset atUtc,
        string currency,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        _ = Money.Zero(currency);

        lock (_gate)
        {
            DateOnly day = ResolveDateUnderLock(context, atUtc);
            BudgetScope[] scopes = ResolveScopes(context, day);
            var result = new Dictionary<BudgetScopeKind, BudgetCounters>(scopes.Length);
            foreach (BudgetScope scope in scopes)
            {
                BucketState bucket = GetRequiredBucket(scope);
                EnsureReservationCurrency(bucket, currency);
                result.Add(
                    scope.Kind,
                    new BudgetCounters(
                        bucket.ReservedMicros,
                        bucket.ConsumedMicros,
                        bucket.CircuitOpen));
            }

            return Task.FromResult<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>>(result);
        }
    }

    private static void ValidateRequest(BudgetReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RequestFingerprint))
            throw new ArgumentException("A request fingerprint is required.", nameof(request));
        if (request.EstimatedCostMicros < 0)
            throw new ArgumentOutOfRangeException(nameof(request.EstimatedCostMicros));
        if (request.MaximumCostMicros < 0)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumCostMicros));
        if (request.EstimatedCostMicros > request.MaximumCostMicros)
            throw new ArgumentOutOfRangeException(nameof(request.EstimatedCostMicros));
        if (string.IsNullOrWhiteSpace(request.PriceVersion))
            throw new ArgumentException("A price version is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProviderKey))
            throw new ArgumentException("A provider key is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModelSnapshot))
            throw new ArgumentException("A model snapshot is required.", nameof(request));
        _ = new Money(request.MaximumCostMicros, request.Currency);
    }

    private BudgetReserveDecision CacheDenied(
        BudgetReservationRequest request,
        ReservationIdentity identity,
        BudgetScopeKind deniedScope,
        string reasonCode)
    {
        var decision = new BudgetReserveDecision(
            Admitted: false,
            Reservation: null,
            DeniedScope: deniedScope,
            Replayed: false,
            ReasonCode: reasonCode);
        _idempotency.Add(
            request.IdempotencyKey,
            new IdempotencyEntry(identity, decision));
        return decision;
    }

    private ReservationIdentity CreateReservationIdentityUnderLock(
        BudgetReservationRequest request)
    {
        DateOnly dailyDate = ResolveDateUnderLock(request.Context, request.RequestedAtUtc);
        return new ReservationIdentity(
            request.RequestFingerprint,
            request.Context.AccountId,
            request.Context.DeviceId,
            request.Context.ChapterId,
            request.Context.ProjectId,
            dailyDate,
            request.EstimatedCostMicros,
            request.MaximumCostMicros,
            request.Currency,
            request.PriceVersion,
            request.ProviderKey,
            request.ModelSnapshot);
    }

    private void ConfigureLimitUnderLock(BudgetScope scope, Money limit)
    {
        EnsureLimitCanBeConfigured(scope, limit);
        if (_buckets.TryGetValue(scope, out BucketState? existing))
        {
            existing.Limit = limit;
        }
        else
        {
            _buckets.Add(scope, new BucketState(limit));
        }
    }

    private void EnsureLimitCanBeConfigured(BudgetScope scope, Money limit)
    {
        if (!_buckets.TryGetValue(scope, out BucketState? existing)) return;

        long usageMicros = checked(existing.ReservedMicros + existing.ConsumedMicros);
        if (usageMicros != 0 &&
            !string.Equals(existing.Limit.Currency, limit.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException();
        }

        if (limit.Micros < usageMicros)
            throw new InvalidOperationException("A budget limit cannot be lower than current usage.");
    }

    private BudgetScope[] ResolveRequestScopesUnderLock(BudgetReservationRequest request)
    {
        DateOnly day = request.Context is BudgetContext explicitContext
            ? explicitContext.UtcBudgetDay
            : ResolveDateUnderLock(request.Context, request.RequestedAtUtc);
        return ResolveScopes(request.Context, day);
    }

    private DateOnly ResolveDateUnderLock(BudgetScopeContext context, DateTimeOffset atUtc)
    {
        if (context is BudgetContext explicitContext) return explicitContext.UtcBudgetDay;
        TimeZoneInfo zone = _dailyTimeZones.TryGetValue(ContextKey.From(context), out TimeZoneInfo? configured)
            ? configured
            : TimeZoneInfo.Utc;
        return DateAt(atUtc, zone);
    }

    private static BudgetScope[] ResolveScopes(BudgetScopeContext context, DateOnly dailyDate) =>
    [
        new(BudgetScopeKind.Account, $"account:{context.AccountId}"),
        new(BudgetScopeKind.Device, $"device:{context.DeviceId}"),
        new(BudgetScopeKind.Chapter, $"chapter:{context.ChapterId}"),
        new(BudgetScopeKind.Daily, $"daily:{context.ProjectId}:{dailyDate:yyyy-MM-dd}"),
        new(BudgetScopeKind.Project, $"project:{context.ProjectId}"),
    ];

    private void ReleaseReservedCounters(ReservationState state)
    {
        foreach (BudgetScope scope in state.Scopes)
        {
            BucketState bucket = GetRequiredBucket(scope);
            EnsureReservationCurrency(bucket, state.Currency);
            if (bucket.ReservedMicros < state.Current.MaximumCostMicros)
                throw new InvalidOperationException("The reservation counters are inconsistent.");
        }

        foreach (BudgetScope scope in state.Scopes)
        {
            _buckets[scope].ReservedMicros -= state.Current.MaximumCostMicros;
        }
    }

    private bool TryReplayEvent(
        string eventId,
        EventKind kind,
        Guid reservationId,
        long? actualCostMicros,
        out BudgetReservation reservation)
    {
        if (!_events.TryGetValue(eventId, out EventEntry? existing))
        {
            reservation = null!;
            return false;
        }

        if (existing.Kind != kind ||
            existing.ReservationId != reservationId ||
            existing.ActualCostMicros != actualCostMicros)
        {
            throw new BudgetEventConflictException();
        }

        reservation = existing.Result;
        return true;
    }

    private void RecordEvent(
        string eventId,
        EventKind kind,
        Guid reservationId,
        long? actualCostMicros,
        BudgetReservation result) =>
        _events.Add(eventId, new EventEntry(kind, reservationId, actualCostMicros, result));

    private BucketState GetRequiredBucket(BudgetScope scope) =>
        _buckets.TryGetValue(scope, out BucketState? bucket)
            ? bucket
            : throw new KeyNotFoundException("The requested budget bucket is not configured.");

    private ReservationState GetRequiredReservation(Guid reservationId) =>
        _reservations.TryGetValue(reservationId, out ReservationState? state)
            ? state
            : throw new KeyNotFoundException("The requested budget reservation does not exist.");

    private static void EnsureReservationHeld(ReservationState state)
    {
        if (!state.HoldsReservation)
            throw new InvalidOperationException("The budget reservation is already terminal.");
    }

    private static void EnsureReservationCurrency(BucketState bucket, string currency)
    {
        if (!string.Equals(bucket.Limit.Currency, currency, StringComparison.Ordinal))
            throw new CurrencyMismatchException();
    }

    private static void RequireEventId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("A budget event id is required.", nameof(eventId));
    }

    private static TimeZoneInfo FindTimeZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A daily budget time zone is required.", nameof(id));

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (string.Equals(id, "Asia/Shanghai", StringComparison.Ordinal))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException) when (string.Equals(id, "China Standard Time", StringComparison.Ordinal))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static DateOnly DateAt(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private sealed class BucketState(Money limit)
    {
        public Money Limit { get; set; } = limit;

        public long ReservedMicros { get; set; }

        public long ConsumedMicros { get; set; }

        public bool CircuitOpen { get; set; }
    }

    private sealed class ReservationState(
        BudgetReservation current,
        BudgetScope[] scopes,
        string currency)
    {
        public BudgetReservation Current { get; set; } = current;

        public BudgetScope[] Scopes { get; } = scopes;

        public string Currency { get; } = currency;

        public bool HoldsReservation { get; set; } = true;
    }

    private sealed record IdempotencyEntry(
        ReservationIdentity Identity,
        BudgetReserveDecision Decision);

    private readonly record struct ReservationIdentity(
        string Fingerprint,
        string AccountId,
        string DeviceId,
        string ChapterId,
        string ProjectId,
        DateOnly DailyDate,
        long EstimatedCostMicros,
        long MaximumCostMicros,
        string Currency,
        string PriceVersion,
        string ProviderKey,
        string ModelSnapshot);

    private sealed record EventEntry(
        EventKind Kind,
        Guid ReservationId,
        long? ActualCostMicros,
        BudgetReservation Result);

    private readonly record struct ContextKey(
        string AccountId,
        string DeviceId,
        string ChapterId,
        string ProjectId)
    {
        public static ContextKey From(BudgetScopeContext context) =>
            new(context.AccountId, context.DeviceId, context.ChapterId, context.ProjectId);
    }

    private enum EventKind
    {
        Settle,
        Release,
        ReconciliationPending,
    }
}
