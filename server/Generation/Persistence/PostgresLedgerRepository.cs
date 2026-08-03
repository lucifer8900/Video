using System.Text.Json;
using Lingmai.RedMist.Generation.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class PostgresLedgerRepository : ILedgerRepository
{
    private const string EventProjection =
        """
        schema_version, entry_id, player_id, event_type, actors, severity,
        chapter, world_clock, source_node_id, fact_refs, payload, received_at_utc,
        ingest_sequence
        """;

    private readonly string _connectionString;

    public PostgresLedgerRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<LedgerAppendResult>> AppendBatchAsync(
        IReadOnlyList<LedgerEventData> events,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        _ = receivedAtUtc;
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] playerHashes = events
                .Select(value => LedgerPlayerTombstone.Hash(value.PlayerId))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (string playerHash in playerHashes)
            {
                await AcquirePlayerLockAsync(
                    connection,
                    transaction,
                    playerHash,
                    cancellationToken).ConfigureAwait(false);
                if (await IsTombstonedAsync(
                        connection,
                        transaction,
                        playerHash,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new LedgerPlayerDeletedException();
                }
            }

            var results = new List<LedgerAppendResult>(events.Count);
            foreach (LedgerEventData value in events)
            {
                string fingerprint = LedgerContentFingerprint.Compute(value);
                InsertResult? inserted = await TryInsertAsync(
                    connection,
                    transaction,
                    value,
                    fingerprint,
                    cancellationToken).ConfigureAwait(false);
                if (inserted is not null)
                {
                    results.Add(new LedgerAppendResult(
                        new NarrativeLedgerEntry(
                            value,
                            inserted.ReceivedAtUtc,
                            inserted.IngestSequence),
                        LedgerAppendDisposition.Accepted));
                    continue;
                }

                (string existingFingerprint, DateTimeOffset existingReceivedAt, long ingestSequence) =
                    await ReadFingerprintAsync(
                        connection,
                        transaction,
                        value.PlayerId,
                        value.EntryId,
                        cancellationToken).ConfigureAwait(false);
                if (!string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new LedgerIdempotencyConflictException();

                results.Add(new LedgerAppendResult(
                    new NarrativeLedgerEntry(value, existingReceivedAt, ingestSequence),
                    LedgerAppendDisposition.Duplicate));
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return results;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LedgerRepositoryPage> QueryAsync(
        string playerId,
        string chapter,
        LedgerPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        int take = checked(limit + 1);
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {EventProjection}
            FROM narrative_ledger_events
            WHERE player_id = @player_id
              AND chapter = @chapter
              AND
              (
                  @has_after = FALSE
                  OR ingest_sequence > @after_ingest_sequence
              )
            ORDER BY ingest_sequence ASC
            LIMIT @take
            """,
            connection);
        Add(command, "player_id", NpgsqlDbType.Text, playerId);
        Add(command, "chapter", NpgsqlDbType.Text, chapter);
        Add(command, "has_after", NpgsqlDbType.Boolean, after is not null);
        Add(command, "after_ingest_sequence", NpgsqlDbType.Bigint, after?.IngestSequence ?? 0L);
        Add(command, "take", NpgsqlDbType.Integer, take);

        var entries = new List<NarrativeLedgerEntry>(take);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            entries.Add(ReadEntry(reader));

        bool hasMore = entries.Count > limit;
        return new LedgerRepositoryPage(entries.Take(limit).ToArray(), hasMore);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM narrative_ledger_events",
            connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)(long)(value ?? 0L));
    }

    public async Task DeletePlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string playerHash = LedgerPlayerTombstone.Hash(playerId);
            await AcquirePlayerLockAsync(
                connection,
                transaction,
                playerHash,
                cancellationToken).ConfigureAwait(false);
            await using (var tombstone = new NpgsqlCommand(
                             """
                             INSERT INTO ledger_player_tombstones (player_hash, deleted_at_utc)
                             VALUES (@player_hash, CURRENT_TIMESTAMP)
                             ON CONFLICT (player_hash) DO NOTHING
                             """,
                             connection,
                             transaction))
            {
                Add(tombstone, "player_hash", NpgsqlDbType.Text, playerHash);
                await tombstone.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var delete = new NpgsqlCommand(
                             "DELETE FROM narrative_ledger_events WHERE player_id = @player_id",
                             connection,
                             transaction))
            {
                Add(delete, "player_id", NpgsqlDbType.Text, playerId);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<InsertResult?> TryInsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerEventData value,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO narrative_ledger_events
            (
                player_id, entry_id, schema_version, event_type, actors, severity,
                chapter, world_clock, source_node_id, fact_refs, payload,
                content_fingerprint, received_at_utc
            )
            VALUES
            (
                @player_id, @entry_id, @schema_version, @event_type, @actors, @severity,
                @chapter, @world_clock, @source_node_id, @fact_refs, @payload,
                @content_fingerprint, CURRENT_TIMESTAMP
            )
            ON CONFLICT (player_id, entry_id) DO NOTHING
            RETURNING ingest_sequence, received_at_utc
            """,
            connection,
            transaction);
        AddEventParameters(command, value, fingerprint);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new InsertResult(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private static async Task<(string Fingerprint, DateTimeOffset ReceivedAtUtc, long IngestSequence)> ReadFingerprintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerId,
        string entryId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT content_fingerprint, received_at_utc, ingest_sequence
            FROM narrative_ledger_events
            WHERE player_id = @player_id AND entry_id = @entry_id
            """,
            connection,
            transaction);
        Add(command, "player_id", NpgsqlDbType.Text, playerId);
        Add(command, "entry_id", NpgsqlDbType.Text, entryId);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The conflicting ledger row could not be read.");
        return (
            reader.GetString(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetInt64(2));
    }

    private static void AddEventParameters(
        NpgsqlCommand command,
        LedgerEventData value,
        string fingerprint)
    {
        Add(command, "player_id", NpgsqlDbType.Text, value.PlayerId);
        Add(command, "entry_id", NpgsqlDbType.Text, value.EntryId);
        Add(command, "schema_version", NpgsqlDbType.Text, value.SchemaVersion);
        Add(command, "event_type", NpgsqlDbType.Text, value.Type);
        Add(command, "actors", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value.Actors));
        Add(command, "severity", NpgsqlDbType.Smallint, checked((short)value.Severity));
        Add(command, "chapter", NpgsqlDbType.Text, value.Chapter);
        Add(command, "world_clock", NpgsqlDbType.Bigint, value.WorldClock);
        Add(command, "source_node_id", NpgsqlDbType.Text, value.SourceNodeId);
        Add(command, "fact_refs", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value.FactRefs));
        Add(command, "payload", NpgsqlDbType.Jsonb, value.Payload.GetRawText());
        Add(command, "content_fingerprint", NpgsqlDbType.Text, fingerprint);
    }

    private static NarrativeLedgerEntry ReadEntry(NpgsqlDataReader reader)
    {
        string[] actors = JsonSerializer.Deserialize<string[]>(reader.GetString(4))
            ?? throw new InvalidDataException("Stored ledger actors are invalid.");
        string[] factRefs = JsonSerializer.Deserialize<string[]>(reader.GetString(9))
            ?? throw new InvalidDataException("Stored ledger fact references are invalid.");
        using JsonDocument payload = JsonDocument.Parse(reader.GetString(10));
        var value = new LedgerEventData(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            actors,
            reader.GetInt16(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetString(8),
            factRefs,
            payload.RootElement.Clone());
        return new NarrativeLedgerEntry(
            value,
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetInt64(12));
    }

    private static async Task AcquirePlayerLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@player_hash, 0))",
            connection,
            transaction);
        Add(command, "player_hash", NpgsqlDbType.Text, playerHash);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsTombstonedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM ledger_player_tombstones WHERE player_hash = @player_hash)",
            connection,
            transaction);
        Add(command, "player_hash", NpgsqlDbType.Text, playerHash);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
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

    private static void Add(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object value) => command.Parameters.AddWithValue(name, type, value);

    private sealed record InsertResult(long IngestSequence, DateTimeOffset ReceivedAtUtc);
}
