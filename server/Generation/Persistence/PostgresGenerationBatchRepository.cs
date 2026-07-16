using Lingmai.RedMist.Generation.Batches;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class PostgresGenerationBatchRepository : IGenerationBatchRepository
{
    private const string BatchProjection =
        """
        id, idempotency_key, request_fingerprint, manifest_id, manifest_content_hash,
        target_tier, source_preview_batch_id, currency, status, created_at_utc,
        updated_at_utc, version
        """;

    private readonly string _connectionString;

    public PostgresGenerationBatchRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<GenerationBatchCreateResult> CreateOrGetAsync(
        GenerationBatchSubmission submission,
        Guid batchId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (batchId == Guid.Empty) throw new ArgumentException("A batch ID is required.", nameof(batchId));
        GenerationBatchPersistenceValidator.EnsureValidItems(submission.Items);

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        bool created;
        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO generation_batches
                (
                    id, idempotency_key, request_fingerprint, manifest_id,
                    manifest_content_hash, target_tier, source_preview_batch_id,
                    currency, status, item_count, run_lease_owner,
                    run_lease_expires_at_utc, version, created_at_utc, updated_at_utc
                )
                VALUES
                (
                    @id, @idempotency_key, @request_fingerprint, @manifest_id,
                    @manifest_content_hash, @target_tier, @source_preview_batch_id,
                    @currency, 'running', @item_count, NULL, NULL, 0, @now_utc, @now_utc
                )
                ON CONFLICT (idempotency_key) DO NOTHING
                RETURNING id
                """,
                connection,
                transaction);
            Add(insert, "id", NpgsqlDbType.Uuid, batchId);
            Add(insert, "idempotency_key", NpgsqlDbType.Text, submission.IdempotencyKey);
            Add(insert, "request_fingerprint", NpgsqlDbType.Text, submission.RequestFingerprint);
            Add(insert, "manifest_id", NpgsqlDbType.Text, submission.ManifestId);
            Add(insert, "manifest_content_hash", NpgsqlDbType.Text, submission.ManifestContentHash);
            Add(insert, "target_tier", NpgsqlDbType.Text, ToDatabaseTier(submission.TargetTier));
            AddNullableUuid(insert, "source_preview_batch_id", submission.SourcePreviewBatchId);
            Add(insert, "currency", NpgsqlDbType.Text, submission.Currency);
            Add(insert, "item_count", NpgsqlDbType.Integer, submission.Items.Count);
            Add(insert, "now_utc", NpgsqlDbType.TimestampTz, normalizedNow);
            created = await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(
                exception.ConstraintName,
                "uq_generation_batches_one_open_manifest",
                StringComparison.Ordinal))
        {
            throw new GenerationBatchGateException(
                "batch.previous_review_pending",
                "The previous batch must be fully reviewed before another batch can start.");
        }

        if (!created)
        {
            (Guid ExistingId, string Fingerprint)? existing = await FindByIdempotencyKeyAsync(
                    connection,
                    transaction,
                    submission.IdempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is null ||
                !string.Equals(
                    existing.Value.Fingerprint,
                    submission.RequestFingerprint,
                    StringComparison.Ordinal))
            {
                throw new GenerationBatchIdempotencyConflictException(submission.IdempotencyKey);
            }

            GenerationBatch replay = await LoadRequiredAsync(
                    connection,
                    transaction,
                    existing.Value.ExistingId,
                    forUpdate: false,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new GenerationBatchCreateResult(replay, Created: false);
        }

        for (int index = 0; index < submission.Items.Count; index++)
        {
            GenerationBatchItem item = submission.Items[index];
            try
            {
                await InsertItemAsync(
                        connection,
                        transaction,
                        batchId,
                        index,
                        submission.SourcePreviewBatchId,
                        item,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(
                    exception.ConstraintName,
                    "uq_generation_batch_items_single_promotion",
                    StringComparison.Ordinal))
            {
                throw new GenerationBatchGateException(
                    "batch.promotion_already_exists",
                    "An adopted preview shot can have only one final-quality promotion batch.");
            }
            foreach (GenerationBatchAttempt attempt in item.Attempts)
            {
                await InsertAttemptAsync(
                        connection,
                        transaction,
                        batchId,
                        item.ShotId,
                        attempt,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        GenerationBatch batch = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new GenerationBatchCreateResult(batch, Created: true);
    }

    public async Task<GenerationBatch?> GetAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await LoadAsync(
                connection,
                transaction: null,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM generation_batches", connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)(long)(value ?? 0L));
    }

    public async Task<GenerationBatchRunLease?> TryAcquireRunAsync(
        Guid batchId,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A batch run owner is required.", nameof(owner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateTimeOffset expiresAtUtc = normalizedNow + leaseDuration;
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE generation_batches
            SET run_lease_owner = @owner,
                run_lease_expires_at_utc = @expires_at_utc,
                run_lease_fencing_token = run_lease_fencing_token + 1
            WHERE id = @id
              AND status = 'running'
              AND
              (
                  run_lease_owner IS NULL
                  OR run_lease_expires_at_utc <= @now_utc
                  OR run_lease_owner = @owner
              )
            RETURNING run_lease_fencing_token
            """,
            connection,
            transaction);
        Add(command, "id", NpgsqlDbType.Uuid, batchId);
        Add(command, "owner", NpgsqlDbType.Text, owner);
        Add(command, "now_utc", NpgsqlDbType.TimestampTz, normalizedNow);
        Add(command, "expires_at_utc", NpgsqlDbType.TimestampTz, expiresAtUtc);
        object? acquired = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (acquired is null)
        {
            GenerationBatch? current = await LoadAsync(
                    connection,
                    transaction,
                    batchId,
                    forUpdate: false,
                    cancellationToken)
                .ConfigureAwait(false);
            if (current is null) throw BatchNotFound(batchId);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        GenerationBatch batch = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        long fencingToken = (long)acquired;
        return new GenerationBatchRunLease(batch, owner, expiresAtUtc, fencingToken);
    }

    public async Task ReleaseRunAsync(
        Guid batchId,
        string owner,
        long fencingToken,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE generation_batches
            SET run_lease_owner = NULL,
                run_lease_expires_at_utc = NULL
            WHERE id = @id
              AND run_lease_owner = @owner
              AND run_lease_fencing_token = @fencing_token
            """,
            connection);
        Add(command, "id", NpgsqlDbType.Uuid, batchId);
        Add(command, "owner", NpgsqlDbType.Text, owner);
        Add(command, "fencing_token", NpgsqlDbType.Bigint, fencingToken);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationBatch> AppendAttemptAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        GenerationBatchAttempt attempt,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shotId);
        ArgumentNullException.ThrowIfNull(attempt);
        GenerationBatchPersistenceValidator.EnsureValidAttempt(attempt);

        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        GenerationBatch current = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Status != GenerationBatchStatus.Running)
        {
            throw new GenerationBatchGateException(
                "batch.not_running",
                "Only a running generation batch can accept attempts.");
        }
        await EnsureRunLeaseAsync(
                connection,
                transaction,
                batchId,
                runLeaseOwner,
                runLeaseFencingToken,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureVersion(current, expectedVersion);

        int itemIndex = FindItemIndex(current.Items, shotId);
        if (itemIndex < 0)
        {
            throw new GenerationBatchValidationException(
                "batch.unknown_shot",
                "The shot is not in this batch.");
        }

        GenerationBatchItem selected = current.Items[itemIndex];
        if (selected.Status != GenerationBatchItemStatus.Pending)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return current;
        }

        await InsertAttemptAsync(
                connection,
                transaction,
                batchId,
                shotId,
                attempt,
                cancellationToken)
            .ConfigureAwait(false);
        await UpdateItemStatusAsync(
                connection,
                transaction,
                batchId,
                shotId,
                GenerationBatchItemStatus.AwaitingReview,
                cancellationToken)
            .ConfigureAwait(false);

        GenerationBatchItem[] updatedItems = current.Items.ToArray();
        updatedItems[itemIndex] = selected with
        {
            Status = GenerationBatchItemStatus.AwaitingReview,
            Attempts = [.. selected.Attempts, attempt],
        };
        await UpdateBatchStateAsync(
                connection,
                transaction,
                batchId,
                expectedVersion,
                StatusFor(updatedItems),
                attempt.CompletedAtUtc,
                cancellationToken)
            .ConfigureAwait(false);

        GenerationBatch updated = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<GenerationBatchReviewApplyResult> ApplyReviewsAsync(
        Guid batchId,
        long expectedVersion,
        string idempotencyKey,
        string reviewFingerprint,
        IReadOnlyList<GenerationBatchItemReview> reviews,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewFingerprint);
        ArgumentNullException.ThrowIfNull(reviews);

        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await AcquireReviewIdempotencyLockAsync(
                connection,
                transaction,
                idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        ReviewReplay? replay = await FindReviewAsync(
                connection,
                transaction,
                idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.BatchId != batchId ||
                !string.Equals(replay.Fingerprint, reviewFingerprint, StringComparison.Ordinal))
            {
                throw new GenerationBatchIdempotencyConflictException(idempotencyKey);
            }

            GenerationBatch replayedBatch = await LoadRequiredAsync(
                    connection,
                    transaction,
                    batchId,
                    forUpdate: false,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new GenerationBatchReviewApplyResult(replayedBatch, Applied: false);
        }

        GenerationBatch current = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: true,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureVersion(current, expectedVersion);
        if (current.Status != GenerationBatchStatus.AwaitingReview)
        {
            throw new GenerationBatchGateException(
                "batch.not_awaiting_review",
                "The batch is not awaiting human review.");
        }

        GenerationBatchItem[] updatedItems = current.Items.ToArray();
        foreach (GenerationBatchItemReview review in reviews)
        {
            int itemIndex = FindItemIndex(updatedItems, review.ShotId);
            if (itemIndex < 0)
            {
                throw new GenerationBatchValidationException(
                    "batch.unknown_shot",
                    "The shot is not in this batch.");
            }

            if (updatedItems[itemIndex].Status != GenerationBatchItemStatus.AwaitingReview)
            {
                throw new GenerationBatchGateException(
                    "batch.item_not_awaiting_review",
                    "The selected shot is not awaiting review.");
            }

            GenerationBatchAttempt? reviewedAttempt = updatedItems[itemIndex].Attempts.LastOrDefault();
            if (reviewedAttempt is null ||
                reviewedAttempt.AttemptNumber != review.ExpectedAttemptNumber ||
                !string.Equals(
                    reviewedAttempt.Artifact.ContentHash,
                    review.ExpectedArtifactHash,
                    StringComparison.Ordinal))
            {
                throw new GenerationBatchGateException(
                    "batch.review_stale_attempt",
                    "The review does not match the latest generated artifact.");
            }

            updatedItems[itemIndex] = updatedItems[itemIndex] with
            {
                Status = StatusFor(review.Decision),
            };
        }

        long reviewId = await InsertReviewAsync(
                connection,
                transaction,
                batchId,
                idempotencyKey,
                reviewFingerprint,
                nowUtc.ToUniversalTime(),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (GenerationBatchItemReview review in reviews)
        {
            await InsertReviewItemAsync(
                    connection,
                    transaction,
                    reviewId,
                    batchId,
                    review,
                    cancellationToken)
                .ConfigureAwait(false);
            await UpdateItemStatusAsync(
                    connection,
                    transaction,
                    batchId,
                    review.ShotId,
                    StatusFor(review.Decision),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await UpdateBatchStateAsync(
                connection,
                transaction,
                batchId,
                expectedVersion,
                StatusFor(updatedItems),
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
        GenerationBatch updated = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new GenerationBatchReviewApplyResult(updated, Applied: true);
    }

    public async Task<GenerationBatch> FailItemAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        string failureCode,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        GenerationBatch current = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Status != GenerationBatchStatus.Running)
        {
            throw new GenerationBatchGateException(
                "batch.not_running",
                "Only a running generation batch can enter failed state.");
        }
        await EnsureRunLeaseAsync(
                connection,
                transaction,
                batchId,
                runLeaseOwner,
                runLeaseFencingToken,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureVersion(current, expectedVersion);
        int itemIndex = FindItemIndex(current.Items, shotId);
        if (itemIndex < 0)
        {
            throw new GenerationBatchValidationException(
                "batch.unknown_shot",
                "The shot is not in this batch.");
        }

        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE generation_batch_items
                         SET status = 'failed',
                             failure_code = CASE
                                 WHEN shot_id = @shot_id THEN @failure_code
                                 ELSE 'batch.skipped_after_failure'
                             END
                         WHERE batch_id = @batch_id
                         """,
                         connection,
                         transaction))
        {
            Add(command, "failure_code", NpgsqlDbType.Text, failureCode);
            Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
            Add(command, "shot_id", NpgsqlDbType.Text, shotId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != current.Items.Count)
            {
                throw new GenerationBatchGateException(
                    "batch.item_not_pending",
                    "Only a pending batch item can enter failed state.");
            }
        }

        await UpdateBatchStateAsync(
                connection,
                transaction,
                batchId,
                expectedVersion,
                GenerationBatchStatus.Failed,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
        GenerationBatch updated = await LoadRequiredAsync(
                connection,
                transaction,
                batchId,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
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

    private static async Task EnsureRunLeaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        string owner,
        long fencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM generation_batches
                WHERE id = @id
                  AND run_lease_owner = @owner
                  AND run_lease_fencing_token = @fencing_token
                  AND run_lease_expires_at_utc > @now_utc
            )
            """,
            connection,
            transaction);
        Add(command, "id", NpgsqlDbType.Uuid, batchId);
        Add(command, "owner", NpgsqlDbType.Text, owner);
        Add(command, "fencing_token", NpgsqlDbType.Bigint, fencingToken);
        Add(command, "now_utc", NpgsqlDbType.TimestampTz, nowUtc.ToUniversalTime());
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not true)
        {
            throw new GenerationBatchGateException(
                "batch.run_lease_lost",
                "The generation batch run lease is missing, expired, or owned by another runner.");
        }
    }

    private static async Task<GenerationBatch?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid batchId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        BatchHeader? header;
        await using (var command = new NpgsqlCommand(
                         $"SELECT {BatchProjection} FROM generation_batches WHERE id = @id" +
                         (forUpdate ? " FOR UPDATE" : string.Empty),
                         connection,
                         transaction))
        {
            Add(command, "id", NpgsqlDbType.Uuid, batchId);
            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            header = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadHeader(reader)
                : null;
        }

        if (header is null) return null;

        var items = new List<GenerationBatchItem>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT shot_id, input_hash, requested_tier, target_tier,
                                target_duration_milliseconds, aspect_ratio,
                                maximum_cost_micros, dialogue_binding,
                                first_frame, last_frame, primary_media, fallback_media,
                                status, failure_code
                         FROM generation_batch_items
                         WHERE batch_id = @batch_id
                         ORDER BY item_order
                         """,
                         connection,
                         transaction))
        {
            Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = new GenerationBatchItem
                {
                    ShotId = reader.GetString(0),
                    InputHash = reader.GetString(1),
                    RequestedTier = FromDatabaseTier(reader.GetString(2)),
                    TargetTier = FromDatabaseTier(reader.GetString(3)),
                    TargetDurationMilliseconds = reader.GetInt32(4),
                    AspectRatio = reader.GetString(5),
                    MaximumCostMicros = reader.GetInt64(6),
                    DialogueBinding = reader.GetString(7),
                    FirstFrame = reader.IsDBNull(8)
                        ? null
                        : DeserializeJson<GenerationBatchFramePin>(reader.GetString(8)),
                    LastFrame = reader.IsDBNull(9)
                        ? null
                        : DeserializeJson<GenerationBatchFramePin>(reader.GetString(9)),
                    PrimaryMedia = reader.IsDBNull(10)
                        ? null
                        : DeserializeJson<GenerationBatchMediaPin>(reader.GetString(10)),
                    FallbackMedia = DeserializeJson<GenerationBatchMediaPin>(reader.GetString(11)),
                    Status = FromDatabaseItemStatus(reader.GetString(12)),
                    FailureCode = reader.IsDBNull(13) ? null : reader.GetString(13),
                    Attempts = [],
                };
                if (!GenerationBatchPersistenceValidator.HasValidPinnedSpec(item))
                {
                    throw new InvalidDataException(
                        "A persisted generation batch item contains an invalid pinned generation spec.");
                }
                items.Add(item);
            }
        }

        var attemptsByShot = new Dictionary<string, List<GenerationBatchAttempt>>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT shot_id, attempt_number, generation_job_id, artifact_id,
                                artifact_media_type, artifact_content_hash,
                                actual_duration_milliseconds, actual_cost_micros,
                                cost_settled, completed_at_utc
                         FROM generation_batch_attempts
                         WHERE batch_id = @batch_id
                         ORDER BY shot_id, attempt_number
                         """,
                         connection,
                         transaction))
        {
            Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string shotId = reader.GetString(0);
                if (!attemptsByShot.TryGetValue(shotId, out List<GenerationBatchAttempt>? attempts))
                {
                    attempts = [];
                    attemptsByShot.Add(shotId, attempts);
                }

                var attempt = new GenerationBatchAttempt(
                    reader.GetInt32(1),
                    reader.GetGuid(2),
                    new GenerationBatchArtifact(
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5)),
                    reader.GetInt32(6),
                    reader.GetInt64(7),
                    reader.GetBoolean(8),
                    reader.GetFieldValue<DateTimeOffset>(9));
                if (!GenerationBatchPersistenceValidator.HasValidAttempt(attempt))
                {
                    throw new InvalidDataException(
                        "A persisted generation batch attempt contains an invalid artifact.");
                }
                attempts.Add(attempt);
            }
        }

        for (int index = 0; index < items.Count; index++)
        {
            GenerationBatchItem item = items[index];
            items[index] = item with
            {
                Attempts = attemptsByShot.TryGetValue(item.ShotId, out List<GenerationBatchAttempt>? attempts)
                    ? attempts.ToArray()
                    : [],
            };
        }

        return new GenerationBatch
        {
            Id = header.Id,
            IdempotencyKey = header.IdempotencyKey,
            RequestFingerprint = header.RequestFingerprint,
            ManifestId = header.ManifestId,
            ManifestContentHash = header.ManifestContentHash,
            TargetTier = header.TargetTier,
            SourcePreviewBatchId = header.SourcePreviewBatchId,
            Currency = header.Currency,
            Status = header.Status,
            Items = items,
            CreatedAtUtc = header.CreatedAtUtc,
            UpdatedAtUtc = header.UpdatedAtUtc,
            Version = header.Version,
        };
    }

    private static async Task<GenerationBatch> LoadRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid batchId,
        bool forUpdate,
        CancellationToken cancellationToken) =>
        await LoadAsync(connection, transaction, batchId, forUpdate, cancellationToken)
            .ConfigureAwait(false)
        ?? throw BatchNotFound(batchId);

    private static async Task<(Guid ExistingId, string Fingerprint)?> FindByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, request_fingerprint
            FROM generation_batches
            WHERE idempotency_key = @idempotency_key
            """,
            connection,
            transaction);
        Add(command, "idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static async Task InsertItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        int itemOrder,
        Guid? sourcePreviewBatchId,
        GenerationBatchItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_batch_items
            (
                batch_id, item_order, shot_id, input_hash, requested_tier,
                target_tier, source_preview_batch_id, source_preview_shot_id,
                target_duration_milliseconds, aspect_ratio,
                maximum_cost_micros, dialogue_binding, first_frame, last_frame,
                primary_media, fallback_media, status, failure_code
            )
            VALUES
            (
                @batch_id, @item_order, @shot_id, @input_hash, @requested_tier,
                @target_tier, @source_preview_batch_id, @source_preview_shot_id,
                @target_duration_milliseconds, @aspect_ratio,
                @maximum_cost_micros, @dialogue_binding, @first_frame, @last_frame,
                @primary_media, @fallback_media, @status, @failure_code
            )
            """,
            connection,
            transaction);
        Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
        Add(command, "item_order", NpgsqlDbType.Integer, itemOrder);
        Add(command, "shot_id", NpgsqlDbType.Text, item.ShotId);
        Add(command, "input_hash", NpgsqlDbType.Text, item.InputHash);
        Add(command, "requested_tier", NpgsqlDbType.Text, ToDatabaseTier(item.RequestedTier));
        Add(command, "target_tier", NpgsqlDbType.Text, ToDatabaseTier(item.TargetTier));
        AddNullableUuid(command, "source_preview_batch_id", sourcePreviewBatchId);
        command.Parameters.AddWithValue(
            "source_preview_shot_id",
            NpgsqlDbType.Text,
            sourcePreviewBatchId is null ? DBNull.Value : item.ShotId);
        Add(command, "target_duration_milliseconds", NpgsqlDbType.Integer, item.TargetDurationMilliseconds);
        Add(command, "aspect_ratio", NpgsqlDbType.Text, item.AspectRatio);
        Add(command, "maximum_cost_micros", NpgsqlDbType.Bigint, item.MaximumCostMicros);
        Add(command, "dialogue_binding", NpgsqlDbType.Text, item.DialogueBinding);
        AddNullableJson(command, "first_frame", item.FirstFrame);
        AddNullableJson(command, "last_frame", item.LastFrame);
        AddNullableJson(command, "primary_media", item.PrimaryMedia);
        Add(command, "fallback_media", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(item.FallbackMedia));
        Add(command, "status", NpgsqlDbType.Text, ToDatabaseItemStatus(item.Status));
        command.Parameters.AddWithValue(
            "failure_code",
            NpgsqlDbType.Text,
            item.FailureCode is null ? DBNull.Value : item.FailureCode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        string shotId,
        GenerationBatchAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_batch_attempts
            (
                batch_id, shot_id, attempt_number, generation_job_id,
                artifact_id, artifact_media_type, artifact_content_hash,
                actual_duration_milliseconds, actual_cost_micros,
                cost_settled, completed_at_utc
            )
            VALUES
            (
                @batch_id, @shot_id, @attempt_number, @generation_job_id,
                @artifact_id, @artifact_media_type, @artifact_content_hash,
                @actual_duration_milliseconds, @actual_cost_micros,
                @cost_settled, @completed_at_utc
            )
            """,
            connection,
            transaction);
        Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
        Add(command, "shot_id", NpgsqlDbType.Text, shotId);
        Add(command, "attempt_number", NpgsqlDbType.Integer, attempt.AttemptNumber);
        Add(command, "generation_job_id", NpgsqlDbType.Uuid, attempt.GenerationJobId);
        Add(command, "artifact_id", NpgsqlDbType.Text, attempt.Artifact.ArtifactId);
        Add(command, "artifact_media_type", NpgsqlDbType.Text, attempt.Artifact.MediaType);
        Add(command, "artifact_content_hash", NpgsqlDbType.Text, attempt.Artifact.ContentHash);
        Add(command, "actual_duration_milliseconds", NpgsqlDbType.Integer, attempt.ActualDurationMilliseconds);
        Add(command, "actual_cost_micros", NpgsqlDbType.Bigint, attempt.ActualCostMicros);
        Add(command, "cost_settled", NpgsqlDbType.Boolean, attempt.CostSettled);
        Add(command, "completed_at_utc", NpgsqlDbType.TimestampTz, attempt.CompletedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateItemStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        string shotId,
        GenerationBatchItemStatus status,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE generation_batch_items
            SET status = @status
            WHERE batch_id = @batch_id AND shot_id = @shot_id
            """,
            connection,
            transaction);
        Add(command, "status", NpgsqlDbType.Text, ToDatabaseItemStatus(status));
        Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
        Add(command, "shot_id", NpgsqlDbType.Text, shotId);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new GenerationBatchValidationException(
                "batch.unknown_shot",
                "The shot is not in this batch.");
        }
    }

    private static async Task UpdateBatchStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        long expectedVersion,
        GenerationBatchStatus status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE generation_batches
            SET status = @status,
                version = version + 1,
                updated_at_utc = @updated_at_utc
            WHERE id = @id AND version = @expected_version
            """,
            connection,
            transaction);
        Add(command, "status", NpgsqlDbType.Text, ToDatabaseBatchStatus(status));
        Add(command, "updated_at_utc", NpgsqlDbType.TimestampTz, updatedAtUtc.ToUniversalTime());
        Add(command, "id", NpgsqlDbType.Uuid, batchId);
        Add(command, "expected_version", NpgsqlDbType.Bigint, expectedVersion);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            GenerationBatch current = await LoadRequiredAsync(
                    connection,
                    transaction,
                    batchId,
                    forUpdate: false,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new GenerationBatchConcurrencyException(batchId, expectedVersion, current.Version);
        }
    }

    private static async Task AcquireReviewIdempotencyLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@idempotency_key, 0))",
            connection,
            transaction);
        Add(command, "idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReviewReplay?> FindReviewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT batch_id, review_fingerprint
            FROM generation_batch_reviews
            WHERE idempotency_key = @idempotency_key
            """,
            connection,
            transaction);
        Add(command, "idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ReviewReplay(reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static async Task<long> InsertReviewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        string idempotencyKey,
        string reviewFingerprint,
        DateTimeOffset reviewedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_batch_reviews
                (batch_id, idempotency_key, review_fingerprint, reviewed_at_utc)
            VALUES
                (@batch_id, @idempotency_key, @review_fingerprint, @reviewed_at_utc)
            RETURNING id
            """,
            connection,
            transaction);
        Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
        Add(command, "idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        Add(command, "review_fingerprint", NpgsqlDbType.Text, reviewFingerprint);
        Add(command, "reviewed_at_utc", NpgsqlDbType.TimestampTz, reviewedAtUtc);
        object result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PostgreSQL did not return the generation batch review row.");
        return (long)result;
    }

    private static async Task InsertReviewItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long reviewId,
        Guid batchId,
        GenerationBatchItemReview review,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_batch_review_items
                (review_id, batch_id, shot_id, decision, expected_attempt_number, expected_artifact_hash)
            VALUES
                (@review_id, @batch_id, @shot_id, @decision, @expected_attempt_number, @expected_artifact_hash)
            """,
            connection,
            transaction);
        Add(command, "review_id", NpgsqlDbType.Bigint, reviewId);
        Add(command, "batch_id", NpgsqlDbType.Uuid, batchId);
        Add(command, "shot_id", NpgsqlDbType.Text, review.ShotId);
        Add(command, "decision", NpgsqlDbType.Text, ToDatabaseDecision(review.Decision));
        Add(command, "expected_attempt_number", NpgsqlDbType.Integer, review.ExpectedAttemptNumber);
        Add(command, "expected_artifact_hash", NpgsqlDbType.Text, review.ExpectedArtifactHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BatchHeader ReadHeader(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        FromDatabaseTier(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.GetString(7),
        FromDatabaseBatchStatus(reader.GetString(8)),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.GetFieldValue<DateTimeOffset>(10),
        reader.GetInt64(11));

    private static void EnsureVersion(GenerationBatch batch, long expectedVersion)
    {
        if (batch.Version != expectedVersion)
        {
            throw new GenerationBatchConcurrencyException(
                batch.Id,
                expectedVersion,
                batch.Version);
        }
    }

    private static int FindItemIndex(IReadOnlyList<GenerationBatchItem> items, string shotId)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index].ShotId, shotId, StringComparison.Ordinal)) return index;
        }

        return -1;
    }

    private static GenerationBatchStatus StatusFor(IReadOnlyList<GenerationBatchItem> items)
    {
        if (items.Any(item => item.Status == GenerationBatchItemStatus.Pending))
            return GenerationBatchStatus.Running;
        if (items.Any(item => item.Status == GenerationBatchItemStatus.AwaitingReview))
            return GenerationBatchStatus.AwaitingReview;
        if (items.All(item => item.Status is GenerationBatchItemStatus.Adopted or GenerationBatchItemStatus.Rejected))
            return GenerationBatchStatus.Reviewed;
        return GenerationBatchStatus.Failed;
    }

    private static GenerationBatchItemStatus StatusFor(GenerationBatchReviewDecision decision) =>
        decision switch
        {
            GenerationBatchReviewDecision.Adopt => GenerationBatchItemStatus.Adopted,
            GenerationBatchReviewDecision.Reject => GenerationBatchItemStatus.Rejected,
            GenerationBatchReviewDecision.Retry => GenerationBatchItemStatus.Pending,
            _ => throw new GenerationBatchValidationException(
                "batch.review_decision",
                "The review decision is invalid."),
        };

    private static string ToDatabaseTier(GenerationBatchTier tier) => tier switch
    {
        GenerationBatchTier.PreviewFast => "preview_fast",
        GenerationBatchTier.FinalQuality => "final_quality",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown generation batch tier."),
    };

    private static GenerationBatchTier FromDatabaseTier(string tier) => tier switch
    {
        "preview_fast" => GenerationBatchTier.PreviewFast,
        "final_quality" => GenerationBatchTier.FinalQuality,
        _ => throw new InvalidDataException($"Unknown generation batch tier '{tier}'."),
    };

    private static string ToDatabaseBatchStatus(GenerationBatchStatus status) => status switch
    {
        GenerationBatchStatus.Running => "running",
        GenerationBatchStatus.AwaitingReview => "awaiting_review",
        GenerationBatchStatus.Reviewed => "reviewed",
        GenerationBatchStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown generation batch status."),
    };

    private static GenerationBatchStatus FromDatabaseBatchStatus(string status) => status switch
    {
        "running" => GenerationBatchStatus.Running,
        "awaiting_review" => GenerationBatchStatus.AwaitingReview,
        "reviewed" => GenerationBatchStatus.Reviewed,
        "failed" => GenerationBatchStatus.Failed,
        _ => throw new InvalidDataException($"Unknown generation batch status '{status}'."),
    };

    private static string ToDatabaseItemStatus(GenerationBatchItemStatus status) => status switch
    {
        GenerationBatchItemStatus.Pending => "pending",
        GenerationBatchItemStatus.AwaitingReview => "awaiting_review",
        GenerationBatchItemStatus.Adopted => "adopted",
        GenerationBatchItemStatus.Rejected => "rejected",
        GenerationBatchItemStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown generation batch item status."),
    };

    private static GenerationBatchItemStatus FromDatabaseItemStatus(string status) => status switch
    {
        "pending" => GenerationBatchItemStatus.Pending,
        "awaiting_review" => GenerationBatchItemStatus.AwaitingReview,
        "adopted" => GenerationBatchItemStatus.Adopted,
        "rejected" => GenerationBatchItemStatus.Rejected,
        "failed" => GenerationBatchItemStatus.Failed,
        _ => throw new InvalidDataException($"Unknown generation batch item status '{status}'."),
    };

    private static string ToDatabaseDecision(GenerationBatchReviewDecision decision) => decision switch
    {
        GenerationBatchReviewDecision.Adopt => "adopt",
        GenerationBatchReviewDecision.Reject => "reject",
        GenerationBatchReviewDecision.Retry => "retry",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown review decision."),
    };

    private static InvalidOperationException BatchNotFound(Guid batchId) =>
        new($"Generation batch '{batchId}' was not found.");

    private static void Add(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object value) => command.Parameters.AddWithValue(name, type, value);

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Uuid,
            value.HasValue ? value.Value : DBNull.Value);

    private static void AddNullableJson<T>(NpgsqlCommand command, string name, T? value)
        where T : class =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Jsonb,
            value is null ? DBNull.Value : JsonSerializer.Serialize(value));

    private static T DeserializeJson<T>(string json)
        where T : class =>
        JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidDataException("A persisted generation batch media pin is invalid.");

    private sealed record BatchHeader(
        Guid Id,
        string IdempotencyKey,
        string RequestFingerprint,
        string ManifestId,
        string ManifestContentHash,
        GenerationBatchTier TargetTier,
        Guid? SourcePreviewBatchId,
        string Currency,
        GenerationBatchStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        long Version);

    private sealed record ReviewReplay(Guid BatchId, string Fingerprint);
}
