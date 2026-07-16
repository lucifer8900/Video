using System.Text.Json;

namespace Lingmai.RedMist.Generation.Ledger;

public sealed record LedgerEventData(
    string SchemaVersion,
    string EntryId,
    string PlayerId,
    string Type,
    IReadOnlyList<string> Actors,
    int Severity,
    string Chapter,
    long WorldClock,
    string SourceNodeId,
    IReadOnlyList<string> FactRefs,
    JsonElement Payload);

public sealed record NarrativeLedgerEntry(
    LedgerEventData Event,
    DateTimeOffset ReceivedAtUtc,
    long IngestSequence);

public enum LedgerAppendDisposition
{
    Accepted,
    Duplicate,
}

public sealed record LedgerAppendResult(
    NarrativeLedgerEntry Entry,
    LedgerAppendDisposition Disposition);

public sealed record LedgerPosition(long IngestSequence);

public sealed record LedgerRepositoryPage(
    IReadOnlyList<NarrativeLedgerEntry> Entries,
    bool HasMore);

public sealed record NarrativeLedgerPage(
    IReadOnlyList<NarrativeLedgerEntry> Entries,
    string? NextCursor);

public sealed class LedgerValidationException : InvalidOperationException
{
    public LedgerValidationException()
        : base("The ledger request is invalid.")
    {
    }
}

public sealed class LedgerIdempotencyConflictException : InvalidOperationException
{
    public LedgerIdempotencyConflictException()
        : base("A ledger idempotency key was reused for different content.")
    {
    }
}

public sealed class LedgerPlayerDeletedException : InvalidOperationException
{
    public LedgerPlayerDeletedException()
        : base("The player ledger has been deleted.")
    {
    }
}
