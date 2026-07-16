using Npgsql;
using NpgsqlTypes;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class PostgresGenerationJobRepository : IGenerationJobRepository
{
    private const string Projection =
        """
        id, idempotency_key, input_hash, status, version, attempt_count,
        lease_owner, lease_expires_at_utc, created_at_utc, updated_at_utc,
        failure_code
        """;

    private readonly string _connectionString;

    public PostgresGenerationJobRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<GenerationJob> CreateOrGetAsync(
        GenerationJobSubmission submission,
        Guid jobId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO generation_jobs
            (
                id, idempotency_key, input_hash, status, version, attempt_count,
                lease_owner, lease_expires_at_utc, created_at_utc, updated_at_utc
            )
            VALUES
            (
                @id, @idempotency_key, @input_hash, 'created', 0, 0,
                NULL, NULL, @now_utc, @now_utc
            )
            ON CONFLICT (idempotency_key) DO UPDATE
                SET idempotency_key = EXCLUDED.idempotency_key
            RETURNING {Projection}
            """,
            connection);
        Add(command, "id", NpgsqlDbType.Uuid, jobId);
        Add(command, "idempotency_key", NpgsqlDbType.Text, submission.IdempotencyKey);
        Add(command, "input_hash", NpgsqlDbType.Text, submission.InputHash);
        Add(command, "now_utc", NpgsqlDbType.TimestampTz, normalizedNow);
        return await ReadRequiredAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationJob?> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"SELECT {Projection} FROM generation_jobs WHERE id = @id",
            connection);
        Add(command, "id", NpgsqlDbType.Uuid, jobId);
        return await ReadOptionalAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM generation_jobs",
            connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)(long)(value ?? 0L));
    }

    public async Task<GenerationJob> TransitionAsync(
        Guid jobId,
        long expectedVersion,
        GenerationJobStatus targetStatus,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        GenerationJob current = await LockRequiredAsync(
                connection,
                transaction,
                jobId,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Version != expectedVersion)
        {
            throw new GenerationJobConcurrencyException(jobId, expectedVersion, current.Version);
        }

        GenerationJobStateMachine.EnsureCanTransition(current.Status, targetStatus);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE generation_jobs
            SET status = @status,
                failure_code = @failure_code,
                version = version + 1,
                lease_owner = NULL,
                lease_expires_at_utc = NULL,
                updated_at_utc = CURRENT_TIMESTAMP
            WHERE id = @id AND version = @expected_version
            RETURNING {Projection}
            """,
            connection,
            transaction);
        Add(command, "status", NpgsqlDbType.Text, ToDatabaseStatus(targetStatus));
        string? failureCode = FailureCodeFor(current.Status, targetStatus);
        command.Parameters.AddWithValue(
            "failure_code",
            NpgsqlDbType.Text,
            failureCode is null ? DBNull.Value : failureCode);
        Add(command, "id", NpgsqlDbType.Uuid, jobId);
        Add(command, "expected_version", NpgsqlDbType.Bigint, expectedVersion);
        GenerationJob updated = await ReadRequiredAsync(command, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<GenerationJobLease?> TryAcquireNextAsync(
        string workerId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateLeaseArguments(workerId, leaseDuration);
        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateTimeOffset expiresAt = normalizedNow + leaseDuration;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            WITH candidate AS
            (
                SELECT id
                FROM generation_jobs
                WHERE status IN ('queued', 'generating', 'moderating', 'transcoding')
                  AND (lease_owner IS NULL OR lease_expires_at_utc <= @now_utc)
                ORDER BY created_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE generation_jobs AS job
            SET lease_owner = @worker_id,
                lease_expires_at_utc = @expires_at_utc,
                attempt_count = job.attempt_count + 1,
                version = job.version + 1,
                updated_at_utc = @now_utc
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING {QualifyProjection("job")}
            """,
            connection,
            transaction);
        Add(command, "now_utc", NpgsqlDbType.TimestampTz, normalizedNow);
        Add(command, "expires_at_utc", NpgsqlDbType.TimestampTz, expiresAt);
        Add(command, "worker_id", NpgsqlDbType.Text, workerId);
        GenerationJob? job = await ReadOptionalAsync(command, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return job is null ? null : new GenerationJobLease(job);
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

    private static async Task<GenerationJob> LockRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {Projection} FROM generation_jobs WHERE id = @id FOR UPDATE",
            connection,
            transaction);
        Add(command, "id", NpgsqlDbType.Uuid, id);
        GenerationJob? job = await ReadOptionalAsync(command, cancellationToken).ConfigureAwait(false);
        return job ?? throw new GenerationJobNotFoundException(id);
    }

    private static async Task<GenerationJob> ReadRequiredAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken) =>
        await ReadOptionalAsync(command, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("PostgreSQL did not return the generation job row.");

    private static async Task<GenerationJob?> ReadOptionalAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new GenerationJob
        {
            Id = reader.GetGuid(0),
            IdempotencyKey = reader.GetString(1),
            InputHash = reader.GetString(2),
            Status = FromDatabaseStatus(reader.GetString(3)),
            Version = reader.GetInt64(4),
            AttemptCount = reader.GetInt32(5),
            LeaseOwner = reader.IsDBNull(6) ? null : reader.GetString(6),
            LeaseExpiresAtUtc = reader.IsDBNull(7)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(7),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(8),
            UpdatedAtUtc = reader.GetFieldValue<DateTimeOffset>(9),
            FailureCode = reader.IsDBNull(10) ? null : reader.GetString(10),
        };
    }

    private static void Add(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object value) => command.Parameters.AddWithValue(name, type, value);

    private static string QualifyProjection(string alias) => string.Join(
        ", ",
        Projection.Split(',', StringSplitOptions.TrimEntries).Select(column => $"{alias}.{column}"));

    private static string ToDatabaseStatus(GenerationJobStatus status) => status switch
    {
        GenerationJobStatus.Created => "created",
        GenerationJobStatus.Queued => "queued",
        GenerationJobStatus.Generating => "generating",
        GenerationJobStatus.Moderating => "moderating",
        GenerationJobStatus.Transcoding => "transcoding",
        GenerationJobStatus.Ready => "ready",
        GenerationJobStatus.Failed => "failed",
        GenerationJobStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown generation status."),
    };

    private static GenerationJobStatus FromDatabaseStatus(string status) => status switch
    {
        "created" => GenerationJobStatus.Created,
        "queued" => GenerationJobStatus.Queued,
        "generating" => GenerationJobStatus.Generating,
        "moderating" => GenerationJobStatus.Moderating,
        "transcoding" => GenerationJobStatus.Transcoding,
        "ready" => GenerationJobStatus.Ready,
        "failed" => GenerationJobStatus.Failed,
        "expired" => GenerationJobStatus.Expired,
        _ => throw new InvalidDataException($"Unknown generation job status '{status}'."),
    };

    private static string? FailureCodeFor(
        GenerationJobStatus currentStatus,
        GenerationJobStatus targetStatus) => targetStatus == GenerationJobStatus.Failed
        ? currentStatus == GenerationJobStatus.Moderating
            ? "moderation.rejected"
            : "generation.failed"
        : null;

    private static void ValidateLeaseArguments(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }
}
