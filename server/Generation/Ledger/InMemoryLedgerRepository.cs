namespace Lingmai.RedMist.Generation.Ledger;

public sealed class InMemoryLedgerRepository : ILedgerRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<LedgerKey, StoredEntry> _entries = [];
    private readonly HashSet<string> _tombstonedPlayerHashes = new(StringComparer.Ordinal);
    private long _nextIngestSequence;

    public Task<IReadOnlyList<LedgerAppendResult>> AppendBatchAsync(
        IReadOnlyList<LedgerEventData> events,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset normalizedNow = receivedAtUtc.ToUniversalTime();

        lock (_gate)
        {
            if (events.Any(value =>
                    _tombstonedPlayerHashes.Contains(LedgerPlayerTombstone.Hash(value.PlayerId))))
            {
                throw new LedgerPlayerDeletedException();
            }

            var staged = new Dictionary<LedgerKey, StoredEntry>();
            var results = new List<LedgerAppendResult>(events.Count);
            long nextIngestSequence = _nextIngestSequence;
            foreach (LedgerEventData value in events)
            {
                string fingerprint = LedgerContentFingerprint.Compute(value);
                var key = new LedgerKey(value.PlayerId, value.EntryId);
                if (_entries.TryGetValue(key, out StoredEntry? existing) ||
                    staged.TryGetValue(key, out existing))
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                        throw new LedgerIdempotencyConflictException();

                    results.Add(new LedgerAppendResult(
                        existing.Entry,
                        LedgerAppendDisposition.Duplicate));
                    continue;
                }

                var stored = new StoredEntry(
                    new NarrativeLedgerEntry(
                        Clone(value),
                        normalizedNow,
                        checked(++nextIngestSequence)),
                    fingerprint);
                staged.Add(key, stored);
                results.Add(new LedgerAppendResult(
                    stored.Entry,
                    LedgerAppendDisposition.Accepted));
            }

            foreach ((LedgerKey key, StoredEntry entry) in staged) _entries.Add(key, entry);
            _nextIngestSequence = nextIngestSequence;
            return Task.FromResult<IReadOnlyList<LedgerAppendResult>>(results);
        }
    }

    public Task<LedgerRepositoryPage> QueryAsync(
        string playerId,
        string chapter,
        LedgerPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            NarrativeLedgerEntry[] candidates = _entries.Values
                .Select(value => value.Entry)
                .Where(entry => string.Equals(entry.Event.PlayerId, playerId, StringComparison.Ordinal))
                .Where(entry => string.Equals(entry.Event.Chapter, chapter, StringComparison.Ordinal))
                .Where(entry => after is null || entry.IngestSequence > after.IngestSequence)
                .OrderBy(entry => entry.IngestSequence)
                .Take(checked(limit + 1))
                .ToArray();
            bool hasMore = candidates.Length > limit;
            return Task.FromResult(new LedgerRepositoryPage(
                candidates.Take(limit).ToArray(),
                hasMore));
        }
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_entries.Count);
    }

    public Task DeletePlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _tombstonedPlayerHashes.Add(LedgerPlayerTombstone.Hash(playerId));
            LedgerKey[] keys = _entries.Keys
                .Where(key => string.Equals(key.PlayerId, playerId, StringComparison.Ordinal))
                .ToArray();
            foreach (LedgerKey key in keys) _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    private static LedgerEventData Clone(LedgerEventData value) => value with
    {
        Actors = value.Actors.ToArray(),
        FactRefs = value.FactRefs.ToArray(),
        Payload = value.Payload.Clone(),
    };

    private sealed record LedgerKey(string PlayerId, string EntryId);

    private sealed record StoredEntry(NarrativeLedgerEntry Entry, string Fingerprint);
}
