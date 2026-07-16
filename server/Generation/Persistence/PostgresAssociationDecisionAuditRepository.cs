using System.Collections.ObjectModel;
using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;
using Npgsql;
using NpgsqlTypes;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class PostgresAssociationDecisionAuditRepository
    : IAssociationDecisionAuditRepository
{
    private const string AuditProjection =
        """
        job.input_hash,
        audit.schema_version,
        audit.audit_ref,
        audit.thread_id,
        audit.player_ref_hash,
        audit.chapter_id,
        audit.template_id,
        audit.resolved_params::text,
        audit.providers::text,
        audit.guard_results::text,
        audit.candidate_count,
        audit.fallback_used,
        audit.outcome_reason,
        audit.latency_ms,
        job.created_at_utc,
        audit.audit_fingerprint
        """;

    private readonly string _connectionString;

    public PostgresAssociationDecisionAuditRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async ValueTask StoreAsync(
        AssociationDecisionAuditContract audit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssociationDecisionAuditContract frozen = NormalizeForPostgres(
            AssociationAuditValidation.Freeze(audit));
        string fingerprint = AssociationAuditValidation.Fingerprint(frozen);
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AcquirePlayerLockAsync(
                    connection,
                    transaction,
                    frozen.PlayerRefHash,
                    cancellationToken)
                .ConfigureAwait(false);
            if (await IsPlayerTombstonedAsync(
                    connection,
                    transaction,
                    frozen.PlayerRefHash,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new AssociationDecisionAuditSuppressedException();
            }

            Guid? jobId = await TryInsertGenerationJobAsync(
                    connection,
                    transaction,
                    frozen,
                    cancellationToken)
                .ConfigureAwait(false);
            if (jobId is null)
            {
                string? existingFingerprint = await GetFingerprintAsync(
                        connection,
                        transaction,
                        frozen.AuditRef,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new AssociationDecisionAuditConflictException(frozen.AuditRef);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await InsertAuditAsync(
                    connection,
                    transaction,
                    jobId.Value,
                    frozen,
                    fingerprint,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AssociationDecisionAuditConflictException(frozen.AuditRef);
        }
        catch (AssociationDecisionAuditConflictException)
        {
            throw;
        }
        catch (AssociationDecisionAuditSuppressedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new AssociationDecisionAuditException(exception);
        }
    }

    public async ValueTask<AssociationDecisionAuditContract?> GetAsync(
        string auditRef,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AssociationAuditValidation.IsAuditRef(auditRef))
            throw new ArgumentException(
                "A valid association audit reference is required.",
                nameof(auditRef));

        try
        {
            await using NpgsqlConnection connection = await OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                $"""
                SELECT {AuditProjection}
                FROM generation_job_association_audits AS audit
                JOIN generation_jobs AS job ON job.id = audit.generation_job_id
                WHERE audit.audit_ref = @audit_ref
                  AND job.workload_kind = 'association_decision'
                  AND job.status = 'ready'
                """,
                connection);
            Add(command, "audit_ref", NpgsqlDbType.Text, auditRef);
            return await ReadOptionalAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new AssociationDecisionAuditException(exception);
        }
    }

    public async ValueTask<int> DeletePlayerAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string playerRefHash = AssociationAuditHash.ComputePlayerRefHash(playerId);
        try
        {
            await using NpgsqlConnection connection = await OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlTransaction transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await AcquirePlayerLockAsync(
                    connection,
                    transaction,
                    playerRefHash,
                    cancellationToken)
                .ConfigureAwait(false);
            await using (var tombstone = new NpgsqlCommand(
                             """
                             INSERT INTO association_audit_player_tombstones
                                 (player_ref_hash, deleted_at_utc)
                             VALUES (@player_ref_hash, CURRENT_TIMESTAMP)
                             ON CONFLICT (player_ref_hash) DO NOTHING
                             """,
                             connection,
                             transaction))
            {
                Add(tombstone, "player_ref_hash", NpgsqlDbType.Text, playerRefHash);
                await tombstone.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = new NpgsqlCommand(
                """
                WITH deleted AS
                (
                    DELETE FROM generation_jobs AS job
                    USING generation_job_association_audits AS audit
                    WHERE job.id = audit.generation_job_id
                      AND job.workload_kind = 'association_decision'
                      AND audit.player_ref_hash = @player_ref_hash
                    RETURNING job.id
                )
                SELECT count(*) FROM deleted
                """,
                connection,
                transaction);
            Add(command, "player_ref_hash", NpgsqlDbType.Text, playerRefHash);
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            int deleted = checked((int)(long)(result ?? 0L));
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new AssociationDecisionAuditException(exception);
        }
    }

    private static async Task<Guid?> TryInsertGenerationJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssociationDecisionAuditContract audit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_jobs
            (
                id,
                idempotency_key,
                input_hash,
                workload_kind,
                status,
                version,
                attempt_count,
                lease_owner,
                lease_expires_at_utc,
                created_at_utc,
                updated_at_utc,
                failure_code
            )
            VALUES
            (
                @id,
                @idempotency_key,
                @input_hash,
                'association_decision',
                'ready',
                0,
                0,
                NULL,
                NULL,
                @created_at_utc,
                @created_at_utc,
                NULL
            )
            ON CONFLICT (idempotency_key) DO NOTHING
            RETURNING id
            """,
            connection,
            transaction);
        Add(command, "id", NpgsqlDbType.Uuid, Guid.NewGuid());
        Add(command, "idempotency_key", NpgsqlDbType.Text, audit.AuditRef);
        Add(command, "input_hash", NpgsqlDbType.Text, audit.InputHash);
        Add(
            command,
            "created_at_utc",
            NpgsqlDbType.TimestampTz,
            audit.CreatedAtUtc.ToUniversalTime());
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is Guid id ? id : null;
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        AssociationDecisionAuditContract audit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_job_association_audits
            (
                generation_job_id,
                schema_version,
                audit_ref,
                audit_fingerprint,
                thread_id,
                player_ref_hash,
                chapter_id,
                template_id,
                resolved_params,
                providers,
                guard_results,
                candidate_count,
                fallback_used,
                outcome_reason,
                latency_ms
            )
            VALUES
            (
                @generation_job_id,
                @schema_version,
                @audit_ref,
                @audit_fingerprint,
                @thread_id,
                @player_ref_hash,
                @chapter_id,
                @template_id,
                @resolved_params,
                @providers,
                @guard_results,
                @candidate_count,
                @fallback_used,
                @outcome_reason,
                @latency_ms
            )
            """,
            connection,
            transaction);
        Add(command, "generation_job_id", NpgsqlDbType.Uuid, jobId);
        Add(command, "schema_version", NpgsqlDbType.Text, audit.SchemaVersion);
        Add(command, "audit_ref", NpgsqlDbType.Text, audit.AuditRef);
        Add(command, "audit_fingerprint", NpgsqlDbType.Text, fingerprint);
        Add(command, "thread_id", NpgsqlDbType.Text, audit.ThreadId);
        Add(command, "player_ref_hash", NpgsqlDbType.Text, audit.PlayerRefHash);
        Add(command, "chapter_id", NpgsqlDbType.Text, audit.ChapterId);
        Add(command, "template_id", NpgsqlDbType.Text, audit.TemplateId);
        Add(
            command,
            "resolved_params",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(audit.ResolvedParams));
        Add(
            command,
            "providers",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(audit.Providers));
        Add(
            command,
            "guard_results",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(audit.GuardResults));
        Add(command, "candidate_count", NpgsqlDbType.Integer, audit.CandidateCount);
        Add(command, "fallback_used", NpgsqlDbType.Boolean, audit.FallbackUsed);
        command.Parameters.AddWithValue(
            "outcome_reason",
            NpgsqlDbType.Text,
            audit.OutcomeReason is null ? DBNull.Value : audit.OutcomeReason);
        Add(command, "latency_ms", NpgsqlDbType.Bigint, audit.LatencyMilliseconds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetFingerprintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string auditRef,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT audit.audit_fingerprint
            FROM generation_job_association_audits AS audit
            JOIN generation_jobs AS job ON job.id = audit.generation_job_id
            WHERE job.idempotency_key = @audit_ref
              AND audit.audit_ref = @audit_ref
              AND job.workload_kind = 'association_decision'
            FOR UPDATE OF job
            """,
            connection,
            transaction);
        Add(command, "audit_ref", NpgsqlDbType.Text, auditRef);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
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

    private static async Task AcquirePlayerLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerRefHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@player_ref_hash, 0))",
            connection,
            transaction);
        Add(command, "player_ref_hash", NpgsqlDbType.Text, playerRefHash);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsPlayerTombstonedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerRefHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM association_audit_player_tombstones
                WHERE player_ref_hash = @player_ref_hash
            )
            """,
            connection,
            transaction);
        Add(command, "player_ref_hash", NpgsqlDbType.Text, playerRefHash);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static async Task<AssociationDecisionAuditContract?> ReadOptionalAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(7)) ?? throw CorruptAudit();
        AssociationProviderAuditContract[] providers =
            JsonSerializer.Deserialize<AssociationProviderAuditContract[]>(reader.GetString(8))
            ?? throw CorruptAudit();
        AssociationGuardResultAuditContract[] guards =
            JsonSerializer.Deserialize<AssociationGuardResultAuditContract[]>(reader.GetString(9))
            ?? throw CorruptAudit();
        var audit = new AssociationDecisionAuditContract(
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(0),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            new ReadOnlyDictionary<string, string>(parameters),
            Array.AsReadOnly(providers),
            Array.AsReadOnly(guards),
            reader.GetInt32(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetInt64(13),
            reader.GetFieldValue<DateTimeOffset>(14));
        AssociationDecisionAuditContract frozen = AssociationAuditValidation.Freeze(audit);
        string persistedFingerprint = reader.GetString(15);
        if (!string.Equals(
                AssociationAuditValidation.Fingerprint(frozen),
                persistedFingerprint,
                StringComparison.Ordinal))
        {
            throw CorruptAudit();
        }

        return frozen;
    }

    private static InvalidDataException CorruptAudit() =>
        new("The persisted association audit is invalid.");

    private static AssociationDecisionAuditContract NormalizeForPostgres(
        AssociationDecisionAuditContract audit)
    {
        DateTimeOffset utc = audit.CreatedAtUtc.ToUniversalTime();
        long postgresTicks = utc.Ticks - utc.Ticks % 10;
        return AssociationAuditValidation.Freeze(audit with
        {
            CreatedAtUtc = new DateTimeOffset(postgresTicks, TimeSpan.Zero),
        });
    }

    private static void Add(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object value) => command.Parameters.AddWithValue(name, type, value);
}
