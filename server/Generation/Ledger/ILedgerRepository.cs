namespace Lingmai.RedMist.Generation.Ledger;

public interface ILedgerRepository
{
    Task<IReadOnlyList<LedgerAppendResult>> AppendBatchAsync(
        IReadOnlyList<LedgerEventData> events,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken);

    Task<LedgerRepositoryPage> QueryAsync(
        string playerId,
        string chapter,
        LedgerPosition? after,
        int limit,
        CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task DeletePlayerAsync(string playerId, CancellationToken cancellationToken);
}
