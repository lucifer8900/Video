using System.Data;
using Lingmai.RedMist.Generation.Budget;
using Npgsql;
using NpgsqlTypes;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class PostgresBudgetRepository : IBudgetRepository
{
    private static readonly BudgetScopeKind[] ScopeKinds = Enum.GetValues<BudgetScopeKind>();
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    public PostgresBudgetRepository(
        string connectionString,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ConfigureLimitsAsync(
        BudgetScopeContext context,
        BudgetLimitPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        _ = Money.Zero(policy.Currency);
        TimeZoneInfo zone = FindTimeZone(policy.DailyTimeZoneId);
        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateOnly localDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(normalizedNow, zone).DateTime);
        BudgetScope[] scopes = OrderScopes(ResolveScopes(context, localDay));

        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
        for (var index = 0; index < scopes.Length; index++)
        {
            BudgetScope scope = scopes[index];
            (DateTimeOffset start, DateTimeOffset end) = scope.Kind == BudgetScopeKind.Daily
                ? DayBounds(localDay, zone)
                : (DateTimeOffset.UnixEpoch, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
            long limit = policy.LimitFor(scope.Kind);
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(policy));

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO budget_limits
                (
                    scope_type, scope_key, period_start_utc, period_end_utc, time_zone_id,
                    currency, limit_micros, reserved_micros, consumed_micros, circuit_open, version
                )
                VALUES
                (
                    @scope_type, @scope_key, @period_start_utc, @period_end_utc, @time_zone_id,
                    @currency, @limit_micros, 0, 0, FALSE, 0
                )
                ON CONFLICT (scope_type, scope_key, currency)
                DO UPDATE SET
                    limit_micros = EXCLUDED.limit_micros,
                    period_end_utc = EXCLUDED.period_end_utc,
                    time_zone_id = EXCLUDED.time_zone_id,
                    version = budget_limits.version + 1
                WHERE budget_limits.reserved_micros + budget_limits.consumed_micros
                      <= EXCLUDED.limit_micros
                """,
                connection,
                transaction);
            Add(command, "scope_type", NpgsqlDbType.Text, ToScope(scope.Kind));
            Add(command, "scope_key", NpgsqlDbType.Text, scope.Key);
            Add(command, "period_start_utc", NpgsqlDbType.TimestampTz, start);
            Add(command, "period_end_utc", NpgsqlDbType.TimestampTz, end);
            Add(command, "time_zone_id", NpgsqlDbType.Text, policy.DailyTimeZoneId);
            Add(command, "currency", NpgsqlDbType.Text, policy.Currency);
            Add(command, "limit_micros", NpgsqlDbType.Bigint, limit);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("A budget limit cannot be lower than current usage.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BudgetReserveDecision> ReserveAsync(
        BudgetReservationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        DateTimeOffset admittedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

        await LockTransactionKeyAsync(
            connection,
            transaction,
            "budget-reservation",
            request.IdempotencyKey,
            cancellationToken).ConfigureAwait(false);

        ExistingReservation? existing = await ReadByIdempotencyAsync(
            connection,
            transaction,
            request.IdempotencyKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureSameRequest(existing, request);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing.ToDecision(replayed: true);
        }

        BudgetScope[] scopes = await ResolveCurrentDailyScopeAsync(
            connection,
            transaction,
            request.Context,
            request.Currency,
            admittedAtUtc,
            cancellationToken).ConfigureAwait(false);

        List<LimitRow> limits = await LockLimitsAsync(
            connection,
            transaction,
            scopes,
            request.Currency,
            admittedAtUtc,
            cancellationToken).ConfigureAwait(false);
        BudgetScopeKind? deniedScope = null;
        string? reason = null;
        foreach (BudgetScope scope in scopes)
        {
            LimitRow? limit = limits.SingleOrDefault(row => row.Kind == scope.Kind);
            if (limit is null)
            {
                deniedScope = scope.Kind;
                reason = "budget.limit_missing";
                break;
            }
            if (limit.CircuitOpen)
            {
                deniedScope = scope.Kind;
                reason = "budget.circuit_open";
                break;
            }
            long used = checked(limit.ReservedMicros + limit.ConsumedMicros);
            if (used > limit.LimitMicros || request.MaximumCostMicros > limit.LimitMicros - used)
            {
                deniedScope = scope.Kind;
                reason = "budget.limit_exceeded";
                break;
            }
        }

        Guid id = Guid.NewGuid();
        if (deniedScope is not null)
        {
            await InsertReservationAsync(
                connection,
                transaction,
                id,
                request,
                scopes,
                "denied",
                deniedScope,
                reason,
                admittedAtUtc,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BudgetReserveDecision(false, null, deniedScope, false, reason);
        }

        foreach (LimitRow limit in limits.OrderBy(row => row.Kind).ThenBy(row => row.ScopeKey, StringComparer.Ordinal))
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE budget_limits
                SET reserved_micros = reserved_micros + @amount,
                    version = version + 1
                WHERE id = @id
                """,
                connection,
                transaction);
            Add(update, "amount", NpgsqlDbType.Bigint, request.MaximumCostMicros);
            Add(update, "id", NpgsqlDbType.Bigint, limit.Id);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertReservationAsync(
            connection,
            transaction,
            id,
            request,
            scopes,
            "reserved",
            null,
            null,
            admittedAtUtc,
            cancellationToken).ConfigureAwait(false);
        foreach (LimitRow limit in limits)
        {
            await using var detail = new NpgsqlCommand(
                """
                INSERT INTO budget_reservation_scopes
                    (reservation_id, budget_limit_id, reserved_micros)
                VALUES (@reservation_id, @budget_limit_id, @reserved_micros)
                """,
                connection,
                transaction);
            Add(detail, "reservation_id", NpgsqlDbType.Uuid, id);
            Add(detail, "budget_limit_id", NpgsqlDbType.Bigint, limit.Id);
            Add(detail, "reserved_micros", NpgsqlDbType.Bigint, request.MaximumCostMicros);
            await detail.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BudgetReserveDecision(
            true,
            NewReservation(id, BudgetReservationStatus.Reserved, request, null, false),
            null,
            false);
    }

    public async Task<bool> TryClaimDispatchAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE budget_reservations
            SET status = 'dispatching', updated_at_utc = CURRENT_TIMESTAMP
            WHERE id = @id AND status = 'reserved'
            """,
            connection);
        Add(command, "id", NpgsqlDbType.Uuid, reservationId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public Task<BudgetReservation> SettleAsync(
        Guid reservationId,
        string eventId,
        long actualCostMicros,
        CancellationToken cancellationToken)
    {
        if (actualCostMicros < 0) throw new ArgumentOutOfRangeException(nameof(actualCostMicros));
        return CompleteAsync(
            reservationId,
            eventId,
            "settled",
            actualCostMicros,
            cancellationToken);
    }

    public Task<BudgetReservation> ReleaseAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) =>
        CompleteAsync(reservationId, eventId, "released", 0, cancellationToken);

    public Task<BudgetReservation> MarkReconciliationPendingAsync(
        Guid reservationId,
        string eventId,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            reservationId,
            eventId,
            "reconciliation_pending",
            null,
            cancellationToken);

    public async Task<IReadOnlyDictionary<BudgetScopeKind, BudgetCounters>> GetCountersAsync(
        BudgetScopeContext context,
        DateTimeOffset atUtc,
        string currency,
        CancellationToken cancellationToken)
    {
        _ = Money.Zero(currency);
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        BudgetScope[] scopes = await ResolveCurrentDailyScopeAsync(
            connection,
            transaction: null,
            context,
            currency,
            atUtc,
            cancellationToken).ConfigureAwait(false);
        List<LimitRow> rows = await ReadLimitsAsync(
            connection,
            transaction: null,
            scopes,
            currency,
            atUtc,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        var result = rows.ToDictionary(
            row => row.Kind,
            row => new BudgetCounters(row.ReservedMicros, row.ConsumedMicros, row.CircuitOpen));
        if (result.Count != ScopeKinds.Length)
            throw new InvalidOperationException("The five budget counters are unavailable.");
        return result;
    }

    private async Task<BudgetReservation> CompleteAsync(
        Guid reservationId,
        string eventId,
        string eventType,
        long? actualMicros,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

        await LockTransactionKeyAsync(
            connection,
            transaction,
            "budget-event",
            eventId,
            cancellationToken).ConfigureAwait(false);

        EventRow? priorEvent = await ReadEventAsync(
            connection,
            transaction,
            eventId,
            cancellationToken).ConfigureAwait(false);
        if (priorEvent is not null)
        {
            if (priorEvent.ReservationId != reservationId ||
                !string.Equals(priorEvent.EventType, eventType, StringComparison.Ordinal) ||
                priorEvent.ActualMicros != actualMicros)
                throw new BudgetEventConflictException();
            ExistingReservation existing = await ReadByIdAsync(
                connection,
                transaction,
                reservationId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing.Reservation!;
        }

        ExistingReservation current = await ReadByIdAsync(
            connection,
            transaction,
            reservationId,
            cancellationToken).ConfigureAwait(false);
        if (current.Reservation is null ||
            current.Reservation.Status is BudgetReservationStatus.Settled or
                BudgetReservationStatus.Released or BudgetReservationStatus.Denied)
            throw new InvalidOperationException("The budget reservation is terminal.");

        List<LimitRow> limits = await LockReservationLimitsAsync(
            connection,
            transaction,
            reservationId,
            cancellationToken).ConfigureAwait(false);
        bool settlement = string.Equals(eventType, "settled", StringComparison.Ordinal);
        bool pending = string.Equals(eventType, "reconciliation_pending", StringComparison.Ordinal);
        bool exceededEstimate = settlement && actualMicros > current.Reservation.EstimatedCostMicros;
        bool exceededMaximum = settlement && actualMicros > current.Reservation.MaximumCostMicros;
        if (!pending)
        {
            foreach (LimitRow limit in limits)
            {
                await using var update = new NpgsqlCommand(
                    """
                    UPDATE budget_limits
                    SET reserved_micros = reserved_micros - @reserved,
                        consumed_micros = consumed_micros + @actual,
                        circuit_open = circuit_open OR @open_circuit,
                        version = version + 1
                    WHERE id = @id AND reserved_micros >= @reserved
                    """,
                    connection,
                    transaction);
                Add(update, "reserved", NpgsqlDbType.Bigint, current.Reservation.MaximumCostMicros);
                Add(update, "actual", NpgsqlDbType.Bigint, settlement ? actualMicros!.Value : 0L);
                Add(update, "open_circuit", NpgsqlDbType.Boolean, exceededMaximum);
                Add(update, "id", NpgsqlDbType.Bigint, limit.Id);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The budget reservation counters are inconsistent.");
            }
        }

        await using (var updateReservation = new NpgsqlCommand(
        """
        UPDATE budget_reservations
        SET status = @status,
            actual_micros = @actual_micros,
            estimator_exceeded = @exceeded_estimate,
            circuit_opened = @exceeded_maximum,
            updated_at_utc = CURRENT_TIMESTAMP
        WHERE id = @id
        """,
        connection,
        transaction))
        {
            Add(updateReservation, "status", NpgsqlDbType.Text, eventType);
            updateReservation.Parameters.AddWithValue(
                "actual_micros",
                NpgsqlDbType.Bigint,
                actualMicros is null ? DBNull.Value : actualMicros.Value);
            Add(updateReservation, "exceeded_estimate", NpgsqlDbType.Boolean, exceededEstimate);
            Add(updateReservation, "exceeded_maximum", NpgsqlDbType.Boolean, exceededMaximum);
            Add(updateReservation, "id", NpgsqlDbType.Uuid, reservationId);
            await updateReservation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insertEvent = new NpgsqlCommand(
        """
        INSERT INTO budget_events
            (event_id, reservation_id, event_type, actual_micros, occurred_at_utc)
        VALUES
            (@event_id, @reservation_id, @event_type, @actual_micros, CURRENT_TIMESTAMP)
        """,
        connection,
        transaction))
        {
            Add(insertEvent, "event_id", NpgsqlDbType.Text, eventId);
            Add(insertEvent, "reservation_id", NpgsqlDbType.Uuid, reservationId);
            Add(insertEvent, "event_type", NpgsqlDbType.Text, eventType);
            insertEvent.Parameters.AddWithValue(
                "actual_micros",
                NpgsqlDbType.Bigint,
                actualMicros is null ? DBNull.Value : actualMicros.Value);
            await insertEvent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return current.Reservation with
        {
            Status = FromStatus(eventType),
            ActualCostMicros = actualMicros,
            EstimatorExceeded = exceededEstimate,
            CircuitOpened = exceededMaximum,
        };
    }

    private static async Task InsertReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        BudgetReservationRequest request,
        BudgetScope[] scopes,
        string status,
        BudgetScopeKind? deniedScope,
        string? reasonCode,
        DateTimeOffset admittedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO budget_reservations
            (
                id, idempotency_key, request_fingerprint,
                account_scope_key, device_scope_key, chapter_scope_key,
                daily_scope_key, project_scope_key, requested_at_utc,
                estimated_micros, maximum_micros, actual_micros, currency,
                price_version, provider_key, model_snapshot,
                status, denied_scope, reason_code, estimator_exceeded, circuit_opened,
                created_at_utc, updated_at_utc
            )
            VALUES
            (
                @id, @idempotency_key, @request_fingerprint,
                @account_scope_key, @device_scope_key, @chapter_scope_key,
                @daily_scope_key, @project_scope_key, @requested_at_utc,
                @estimated_micros, @maximum_micros, @actual_micros, @currency,
                @price_version, @provider_key, @model_snapshot,
                @status, @denied_scope, @reason_code, FALSE, FALSE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
            """,
            connection,
            transaction);
        Add(command, "id", NpgsqlDbType.Uuid, id);
        Add(command, "idempotency_key", NpgsqlDbType.Text, request.IdempotencyKey);
        Add(command, "request_fingerprint", NpgsqlDbType.Text, request.RequestFingerprint);
        Add(command, "account_scope_key", NpgsqlDbType.Text, Scope(scopes, BudgetScopeKind.Account).Key);
        Add(command, "device_scope_key", NpgsqlDbType.Text, Scope(scopes, BudgetScopeKind.Device).Key);
        Add(command, "chapter_scope_key", NpgsqlDbType.Text, Scope(scopes, BudgetScopeKind.Chapter).Key);
        Add(command, "daily_scope_key", NpgsqlDbType.Text, Scope(scopes, BudgetScopeKind.Daily).Key);
        Add(command, "project_scope_key", NpgsqlDbType.Text, Scope(scopes, BudgetScopeKind.Project).Key);
        Add(command, "requested_at_utc", NpgsqlDbType.TimestampTz, admittedAtUtc.ToUniversalTime());
        Add(command, "estimated_micros", NpgsqlDbType.Bigint, request.EstimatedCostMicros);
        Add(command, "maximum_micros", NpgsqlDbType.Bigint, request.MaximumCostMicros);
        command.Parameters.AddWithValue(
            "actual_micros",
            NpgsqlDbType.Bigint,
            string.Equals(status, "denied", StringComparison.Ordinal) ? 0L : DBNull.Value);
        Add(command, "currency", NpgsqlDbType.Text, request.Currency);
        Add(command, "price_version", NpgsqlDbType.Text, request.PriceVersion);
        Add(command, "provider_key", NpgsqlDbType.Text, request.ProviderKey);
        Add(command, "model_snapshot", NpgsqlDbType.Text, request.ModelSnapshot);
        Add(command, "status", NpgsqlDbType.Text, status);
        command.Parameters.AddWithValue(
            "denied_scope",
            NpgsqlDbType.Text,
            deniedScope is null ? DBNull.Value : ToScope(deniedScope.Value));
        command.Parameters.AddWithValue(
            "reason_code",
            NpgsqlDbType.Text,
            reasonCode is null ? DBNull.Value : reasonCode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<LimitRow>> LockLimitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BudgetScope[] scopes,
        string currency,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await ReadLimitsAsync(
            connection,
            transaction,
            scopes,
            currency,
            atUtc,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

    private static async Task<BudgetScope[]> ResolveCurrentDailyScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        BudgetScopeContext context,
        string currency,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        string prefix = $"daily:{context.ProjectId}:";
        await using var command = new NpgsqlCommand(
            """
            SELECT scope_key
            FROM budget_limits
            WHERE scope_type = 'daily'
              AND currency = @currency
              AND left(scope_key, length(@prefix)) = @prefix
              AND @at_utc >= period_start_utc
              AND @at_utc < period_end_utc
            ORDER BY scope_key
            """,
            connection,
            transaction);
        Add(command, "currency", NpgsqlDbType.Text, currency);
        Add(command, "prefix", NpgsqlDbType.Text, prefix);
        Add(command, "at_utc", NpgsqlDbType.TimestampTz, atUtc.ToUniversalTime());

        var dailyKeys = new List<string>(capacity: 2);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            dailyKeys.Add(reader.GetString(0));
        if (dailyKeys.Count > 1)
            throw new InvalidOperationException("Multiple active daily budget periods were found.");

        DateOnly utcDay = DateOnly.FromDateTime(atUtc.UtcDateTime);
        BudgetScope[] scopes = ResolveScopes(context, utcDay);
        if (dailyKeys.Count == 1)
        {
            int dailyIndex = Array.FindIndex(
                scopes,
                scope => scope.Kind == BudgetScopeKind.Daily);
            scopes[dailyIndex] = new BudgetScope(BudgetScopeKind.Daily, dailyKeys[0]);
        }
        return OrderScopes(scopes);
    }

    private static async Task LockTransactionKeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string domain,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0))",
            connection,
            transaction);
        Add(command, "lock_key", NpgsqlDbType.Text, $"{domain}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<LimitRow>> ReadLimitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        BudgetScope[] scopes,
        string currency,
        DateTimeOffset atUtc,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var clauses = new List<string>();
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        for (var index = 0; index < scopes.Length; index++)
        {
            clauses.Add($"(scope_type = @type_{index} AND scope_key = @key_{index})");
            Add(command, $"type_{index}", NpgsqlDbType.Text, ToScope(scopes[index].Kind));
            Add(command, $"key_{index}", NpgsqlDbType.Text, scopes[index].Key);
        }
        Add(command, "currency", NpgsqlDbType.Text, currency);
        Add(command, "at_utc", NpgsqlDbType.TimestampTz, atUtc.ToUniversalTime());
        command.CommandText =
            $"""
            SELECT id, scope_type, scope_key, limit_micros, reserved_micros,
                   consumed_micros, circuit_open
            FROM budget_limits
            WHERE currency = @currency
              AND @at_utc >= period_start_utc AND @at_utc < period_end_utc
              AND ({string.Join(" OR ", clauses)})
            ORDER BY
                CASE scope_type
                    WHEN 'account' THEN 0
                    WHEN 'device' THEN 1
                    WHEN 'chapter' THEN 2
                    WHEN 'daily' THEN 3
                    WHEN 'project' THEN 4
                    ELSE 5
                END,
                scope_key
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        var result = new List<LimitRow>();
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new LimitRow(
                reader.GetInt64(0),
                FromScope(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetBoolean(6)));
        }
        return result;
    }

    private static async Task<List<LimitRow>> LockReservationLimitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT limits.id, limits.scope_type, limits.scope_key, limits.limit_micros,
                   limits.reserved_micros, limits.consumed_micros, limits.circuit_open
            FROM budget_limits AS limits
            JOIN budget_reservation_scopes AS scopes ON scopes.budget_limit_id = limits.id
            WHERE scopes.reservation_id = @reservation_id
            ORDER BY
                CASE limits.scope_type
                    WHEN 'account' THEN 0
                    WHEN 'device' THEN 1
                    WHEN 'chapter' THEN 2
                    WHEN 'daily' THEN 3
                    WHEN 'project' THEN 4
                    ELSE 5
                END,
                limits.scope_key
            FOR UPDATE OF limits
            """,
            connection,
            transaction);
        Add(command, "reservation_id", NpgsqlDbType.Uuid, reservationId);
        var result = new List<LimitRow>();
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new LimitRow(
                reader.GetInt64(0), FromScope(reader.GetString(1)), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetBoolean(6)));
        if (result.Count != 5) throw new InvalidOperationException("A reservation must hold five scopes.");
        return result;
    }

    private static async Task<ExistingReservation?> ReadByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = ReservationSelect(
            connection,
            transaction,
            "WHERE idempotency_key = @value FOR UPDATE");
        Add(command, "value", NpgsqlDbType.Text, key);
        return await ReadExistingAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExistingReservation> ReadByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = ReservationSelect(
            connection,
            transaction,
            "WHERE id = @value FOR UPDATE");
        Add(command, "value", NpgsqlDbType.Uuid, id);
        return await ReadExistingAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The requested budget reservation does not exist.");
    }

    private static NpgsqlCommand ReservationSelect(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string suffix) => new(
        $"""
        SELECT id, idempotency_key, request_fingerprint,
               account_scope_key, device_scope_key, chapter_scope_key, daily_scope_key, project_scope_key,
               estimated_micros, maximum_micros, actual_micros, currency,
               price_version, provider_key, model_snapshot,
               status, denied_scope, reason_code, estimator_exceeded, circuit_opened
        FROM budget_reservations
        {suffix}
        """,
        connection,
        transaction);

    private static async Task<ExistingReservation?> ReadExistingAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        Guid id = reader.GetGuid(0);
        string status = reader.GetString(15);
        BudgetReservation? reservation = status == "denied"
            ? null
            : new BudgetReservation(
                id,
                FromStatus(status),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.GetBoolean(18),
                reader.GetBoolean(19),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14));
        return new ExistingReservation(
            id,
            reader.GetString(1),
            reader.GetString(2),
            [reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)],
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            status,
            reader.IsDBNull(16) ? null : FromScope(reader.GetString(16)),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reservation);
    }

    private static async Task<EventRow?> ReadEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT reservation_id, event_type, actual_micros
            FROM budget_events WHERE event_id = @event_id FOR UPDATE
            """,
            connection,
            transaction);
        Add(command, "event_id", NpgsqlDbType.Text, eventId);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new EventRow(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    private static void EnsureSameRequest(
        ExistingReservation existing,
        BudgetReservationRequest request)
    {
        string[] keys = existing.ScopeKeys;
        bool sameScopes =
            string.Equals(keys[0], $"account:{request.Context.AccountId}", StringComparison.Ordinal) &&
            string.Equals(keys[1], $"device:{request.Context.DeviceId}", StringComparison.Ordinal) &&
            string.Equals(keys[2], $"chapter:{request.Context.ChapterId}", StringComparison.Ordinal) &&
            keys[3].StartsWith($"daily:{request.Context.ProjectId}:", StringComparison.Ordinal) &&
            string.Equals(keys[4], $"project:{request.Context.ProjectId}", StringComparison.Ordinal);
        if (!string.Equals(existing.Fingerprint, request.RequestFingerprint, StringComparison.Ordinal) ||
            existing.EstimatedMicros != request.EstimatedCostMicros ||
            existing.MaximumMicros != request.MaximumCostMicros ||
            !string.Equals(existing.Currency, request.Currency, StringComparison.Ordinal) ||
            !string.Equals(existing.PriceVersion, request.PriceVersion, StringComparison.Ordinal) ||
            !string.Equals(existing.ProviderKey, request.ProviderKey, StringComparison.Ordinal) ||
            !string.Equals(existing.ModelSnapshot, request.ModelSnapshot, StringComparison.Ordinal) ||
            !sameScopes)
            throw new BudgetIdempotencyConflictException(request.IdempotencyKey);
    }

    private static void ValidateRequest(BudgetReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestFingerprint);
        if (request.EstimatedCostMicros < 0 ||
            request.MaximumCostMicros < 0 ||
            request.EstimatedCostMicros > request.MaximumCostMicros)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.PriceVersion) ||
            string.IsNullOrWhiteSpace(request.ProviderKey) ||
            string.IsNullOrWhiteSpace(request.ModelSnapshot))
            throw new ArgumentException("Budget cost audit metadata is required.", nameof(request));
        _ = new Money(request.MaximumCostMicros, request.Currency);
    }

    private static BudgetScope[] ResolveScopes(BudgetScopeContext context, DateOnly day) =>
    [
        new(BudgetScopeKind.Account, $"account:{context.AccountId}"),
        new(BudgetScopeKind.Device, $"device:{context.DeviceId}"),
        new(BudgetScopeKind.Chapter, $"chapter:{context.ChapterId}"),
        new(BudgetScopeKind.Daily, $"daily:{context.ProjectId}:{day:yyyy-MM-dd}"),
        new(BudgetScopeKind.Project, $"project:{context.ProjectId}"),
    ];

    private static BudgetScope[] OrderScopes(IEnumerable<BudgetScope> scopes) =>
        scopes
            .OrderBy(scope => ScopeRank(scope.Kind))
            .ThenBy(scope => scope.Key, StringComparer.Ordinal)
            .ToArray();

    private static int ScopeRank(BudgetScopeKind kind) => kind switch
    {
        BudgetScopeKind.Account => 0,
        BudgetScopeKind.Device => 1,
        BudgetScopeKind.Chapter => 2,
        BudgetScopeKind.Daily => 3,
        BudgetScopeKind.Project => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BudgetScope Scope(IEnumerable<BudgetScope> scopes, BudgetScopeKind kind) =>
        scopes.Single(scope => scope.Kind == kind);

    private static BudgetReservation NewReservation(
        Guid id,
        BudgetReservationStatus status,
        BudgetReservationRequest request,
        long? actual,
        bool exceeded) => new(
        id, status, request.EstimatedCostMicros, request.MaximumCostMicros,
        actual, exceeded, exceeded,
        request.PriceVersion, request.ProviderKey, request.ModelSnapshot);

    private static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day, TimeZoneInfo zone)
    {
        DateTime startLocal = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime endLocal = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, zone), TimeSpan.Zero),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, zone), TimeSpan.Zero));
    }

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id == "Asia/Shanghai")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException) when (id == "China Standard Time")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void Add(NpgsqlCommand command, string name, NpgsqlDbType type, object value) =>
        command.Parameters.AddWithValue(name, type, value);

    private static string ToScope(BudgetScopeKind kind) => kind.ToString().ToLowerInvariant();

    private static BudgetScopeKind FromScope(string value) => value switch
    {
        "account" => BudgetScopeKind.Account,
        "device" => BudgetScopeKind.Device,
        "chapter" => BudgetScopeKind.Chapter,
        "daily" => BudgetScopeKind.Daily,
        "project" => BudgetScopeKind.Project,
        _ => throw new InvalidDataException("Unknown budget scope type."),
    };

    private static BudgetReservationStatus FromStatus(string value) => value switch
    {
        "reserved" => BudgetReservationStatus.Reserved,
        "dispatching" => BudgetReservationStatus.Dispatching,
        "reconciliation_pending" => BudgetReservationStatus.ReconciliationPending,
        "settled" => BudgetReservationStatus.Settled,
        "released" => BudgetReservationStatus.Released,
        "denied" => BudgetReservationStatus.Denied,
        _ => throw new InvalidDataException("Unknown budget reservation status."),
    };

    private sealed record LimitRow(
        long Id,
        BudgetScopeKind Kind,
        string ScopeKey,
        long LimitMicros,
        long ReservedMicros,
        long ConsumedMicros,
        bool CircuitOpen);

    private sealed record EventRow(Guid ReservationId, string EventType, long? ActualMicros);

    private sealed record ExistingReservation(
        Guid Id,
        string IdempotencyKey,
        string Fingerprint,
        string[] ScopeKeys,
        long EstimatedMicros,
        long MaximumMicros,
        string Currency,
        string PriceVersion,
        string ProviderKey,
        string ModelSnapshot,
        string Status,
        BudgetScopeKind? DeniedScope,
        string? ReasonCode,
        BudgetReservation? Reservation)
    {
        public BudgetReserveDecision ToDecision(bool replayed) =>
            new(Status != "denied", Reservation, DeniedScope, replayed, ReasonCode);
    }
}
