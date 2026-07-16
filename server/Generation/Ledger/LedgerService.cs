using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Ledger;

namespace Lingmai.RedMist.Generation.Ledger;

public sealed class LedgerService
{
    public const string SchemaVersion = "1.0.0";
    public const int MaximumBatchSize = 50;
    public const int MaximumPageSize = 100;
    public const int MaximumPayloadBytes = 4096;
    public const int MaximumEventBytes = 16384;

    private static readonly Regex EntryIdPattern = new(
        "^led\\.[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlayerIdPattern = new(
        "^p\\.[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GenericIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ChapterPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern = new(
        "^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILedgerRepository _repository;
    private readonly TimeProvider _timeProvider;

    public LedgerService(ILedgerRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<IReadOnlyList<LedgerAppendResult>> AppendAsync(
        IReadOnlyList<LedgerEventData> events,
        CancellationToken cancellationToken)
    {
        ValidateBatch(events);
        return _repository.AppendBatchAsync(
            events,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            cancellationToken);
    }

    public async Task<NarrativeLedgerPage> QueryAsync(
        string playerId,
        string chapter,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        ValidateChapter(chapter);
        if (limit is < 1 or > MaximumPageSize) throw new LedgerValidationException();

        LedgerPosition? after = DecodeCursor(playerId, chapter, cursor);
        LedgerRepositoryPage page = await _repository.QueryAsync(
            playerId,
            chapter,
            after,
            limit,
            cancellationToken).ConfigureAwait(false);
        string? nextCursor = page.HasMore && page.Entries.Count > 0
            ? EncodeCursor(playerId, chapter, page.Entries[^1].IngestSequence)
            : null;
        return new NarrativeLedgerPage(page.Entries, nextCursor);
    }

    private static void ValidateBatch(IReadOnlyList<LedgerEventData> events)
    {
        if (events is null || events.Count is < 1 or > MaximumBatchSize)
            throw new LedgerValidationException();

        string? playerId = null;
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LedgerEventData value in events)
        {
            if (value is null) throw new LedgerValidationException();
            ValidateEvent(value);
            playerId ??= value.PlayerId;
            if (!string.Equals(playerId, value.PlayerId, StringComparison.Ordinal) ||
                !entryIds.Add(value.EntryId))
            {
                throw new LedgerValidationException();
            }
        }
    }

    private static void ValidateEvent(LedgerEventData value)
    {
        if (!string.Equals(value.SchemaVersion, SchemaVersion, StringComparison.Ordinal) ||
            !IsMatch(value.EntryId, 5, 96, EntryIdPattern))
        {
            throw new LedgerValidationException();
        }

        ValidatePlayerId(value.PlayerId);
        if (!LedgerEventTypes.All.Contains(value.Type) ||
            value.Actors is null || value.Actors.Count is < 1 or > 8 ||
            value.Actors.Distinct(StringComparer.Ordinal).Count() != value.Actors.Count ||
            value.Actors.Any(actor => !IsMatch(actor, 1, 160, GenericIdPattern)) ||
            value.Severity is < 1 or > 5)
        {
            throw new LedgerValidationException();
        }

        ValidateChapter(value.Chapter);
        if (value.WorldClock is < 0 or > int.MaxValue ||
            !IsMatch(value.SourceNodeId, 1, 160, GenericIdPattern) ||
            value.FactRefs is null || value.FactRefs.Count > 16 ||
            value.FactRefs.Distinct(StringComparer.Ordinal).Count() != value.FactRefs.Count ||
            value.FactRefs.Any(factRef =>
                !IsMatch(factRef, 6, 160, GenericIdPattern) ||
                !factRef.StartsWith("fact.", StringComparison.Ordinal)))
        {
            throw new LedgerValidationException();
        }

        ValidatePayload(value.Type, value.Payload);
        if (JsonSerializer.SerializeToUtf8Bytes(value).Length > MaximumEventBytes)
            throw new LedgerValidationException();
    }

    private static void ValidatePayload(string eventType, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumPayloadBytes)
        {
            throw new LedgerValidationException();
        }

        JsonProperty[] properties = payload.EnumerateObject().ToArray();
        if (properties.Length > 2 ||
            properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
        {
            throw new LedgerValidationException();
        }

        foreach (JsonProperty property in properties)
        {
            bool valid = (eventType, property.Name) switch
            {
                (LedgerEventTypes.DebtIncurred, "debtKind") => IsToken(property.Value),
                (LedgerEventTypes.DebtRepaid, "debtRef") => IsLedgerId(property.Value),
                (LedgerEventTypes.SecretExposed, "secretRef") => IsStableId(property.Value),
                (LedgerEventTypes.PromiseMade, "promiseRef") => IsStableId(property.Value),
                (LedgerEventTypes.PromiseBroken, "promiseRef") => IsStableId(property.Value),
                (LedgerEventTypes.NpcRescued, "npcRef") => IsStableId(property.Value),
                (LedgerEventTypes.NpcAbandoned, "npcRef") => IsStableId(property.Value),
                (LedgerEventTypes.EnemySpared, "npcRef") => IsStableId(property.Value),
                (LedgerEventTypes.ItemGained, "itemRef") => IsStableId(property.Value),
                (LedgerEventTypes.ItemGained, "quantity") => IsQuantity(property.Value),
                (LedgerEventTypes.QuestExpired, "questRef") => IsStableId(property.Value),
                (LedgerEventTypes.TrumpCardRevealed, "abilityRef") => IsStableId(property.Value),
                _ => false,
            };
            if (!valid) throw new LedgerValidationException();
        }
    }

    private static void ValidatePlayerId(string playerId)
    {
        if (!IsMatch(playerId, 3, 96, PlayerIdPattern)) throw new LedgerValidationException();
    }

    private static void ValidateChapter(string chapter)
    {
        if (!IsMatch(chapter, 1, 64, ChapterPattern)) throw new LedgerValidationException();
    }

    private static bool IsMatch(string? value, int minimum, int maximum, Regex pattern) =>
        value is not null && value.Length >= minimum && value.Length <= maximum && pattern.IsMatch(value);

    private static bool IsToken(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 1, 64, TokenPattern);

    private static bool IsStableId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 1, 160, GenericIdPattern);

    private static bool IsLedgerId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 5, 96, EntryIdPattern);

    private static bool IsQuantity(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal quantity) &&
        quantity == decimal.Truncate(quantity) &&
        quantity is >= 1m and <= 999_999m;

    private static string EncodeCursor(string playerId, string chapter, long ingestSequence)
    {
        string clear = string.Join(
            '\n',
            SchemaVersion,
            playerId,
            chapter,
            ingestSequence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(clear))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static LedgerPosition? DecodeCursor(string playerId, string chapter, string? cursor)
    {
        if (cursor is null) return null;
        if (cursor.Length is < 1 or > 256 || cursor.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new LedgerValidationException();
        }

        try
        {
            string encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded += new string('=', (4 - encoded.Length % 4) % 4);
            string[] fields = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('\n');
            if (fields.Length != 4 ||
                !string.Equals(fields[0], SchemaVersion, StringComparison.Ordinal) ||
                !string.Equals(fields[1], playerId, StringComparison.Ordinal) ||
                !string.Equals(fields[2], chapter, StringComparison.Ordinal) ||
                !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long ingestSequence) ||
                ingestSequence < 1)
            {
                throw new LedgerValidationException();
            }

            return new LedgerPosition(ingestSequence);
        }
        catch (FormatException)
        {
            throw new LedgerValidationException();
        }
    }
}
