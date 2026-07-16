using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Ledger;

public static class LedgerEventTypes
{
    public const string DebtIncurred = "debt_incurred";
    public const string DebtRepaid = "debt_repaid";
    public const string SecretExposed = "secret_exposed";
    public const string PromiseMade = "promise_made";
    public const string PromiseBroken = "promise_broken";
    public const string NpcRescued = "npc_rescued";
    public const string NpcAbandoned = "npc_abandoned";
    public const string EnemySpared = "enemy_spared";
    public const string ItemGained = "item_gained";
    public const string QuestExpired = "quest_expired";
    public const string TrumpCardRevealed = "trump_card_revealed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DebtIncurred,
        DebtRepaid,
        SecretExposed,
        PromiseMade,
        PromiseBroken,
        NpcRescued,
        NpcAbandoned,
        EnemySpared,
        ItemGained,
        QuestExpired,
        TrumpCardRevealed,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LedgerEventContract(
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LedgerEventBatchRequest(
    string SchemaVersion,
    IReadOnlyList<LedgerEventContract> Events);

public sealed record LedgerEventAcknowledgement(
    string PlayerId,
    string EntryId,
    string Status);

public sealed record LedgerEventBatchResponse(
    string SchemaVersion,
    IReadOnlyList<LedgerEventAcknowledgement> Acknowledgements);

public sealed record NarrativeLedgerEntryContract(
    LedgerEventContract Event,
    DateTimeOffset ReceivedAtUtc);

public sealed record NarrativeLedgerPageResponse(
    string SchemaVersion,
    IReadOnlyList<NarrativeLedgerEntryContract> Entries,
    string? NextCursor);
