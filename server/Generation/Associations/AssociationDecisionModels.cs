using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Associations;

internal static class AssociationDecisionLimits
{
    internal const int MaximumActiveFactCount = 128;
    internal const int MaximumLedgerEntryCount = 512;
    internal const int MaximumTriggerHistoryCount = 256;
    internal const int MaximumTemplateCount = 256;
}

public sealed record AssociationRouteContext(
    string UpcomingNodeId,
    string UpcomingLocationId,
    IReadOnlyList<string> AllowedInjectionPoints);

public sealed record AssociationTriggerHistory(
    string TemplateId,
    long LastTriggeredWorldClock,
    int TriggerCount);

public sealed class AssociationRuntimePolicy
{
    private AssociationRuntimePolicy(bool allowRuntimeGeneration)
    {
        AllowRuntimeGeneration = allowRuntimeGeneration;
    }

    public static AssociationRuntimePolicy ReuseOnly { get; } = new(false);

    public bool AllowRuntimeGeneration { get; }

    internal static AssociationRuntimePolicy CreateForTests(bool allowRuntimeGeneration) =>
        new(allowRuntimeGeneration);
}

public sealed record AssociationDecisionRequest(
    string PlayerId,
    string Chapter,
    long WorldClock,
    AssociationRouteContext Route,
    IReadOnlyList<string> ActiveFactIds,
    IReadOnlyList<NarrativeLedgerEntry> LedgerEntries,
    IReadOnlyList<AssociationTriggerHistory> TriggerHistory,
    string DefaultFallbackThreadId,
    AssociationRuntimePolicy? RuntimePolicy = null)
{
    public AssociationRuntimePolicy EffectiveRuntimePolicy =>
        RuntimePolicy ?? AssociationRuntimePolicy.ReuseOnly;
}

public sealed class AssociationRuleCandidate
{
    internal AssociationRuleCandidate(
        ApprovedAssociationTemplate template,
        IReadOnlyDictionary<string, string> resolvedParameters,
        NarrativeLedgerEntry primaryEvidence,
        string injectionPoint)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        ArgumentNullException.ThrowIfNull(resolvedParameters);
        ArgumentNullException.ThrowIfNull(primaryEvidence);
        if (string.IsNullOrWhiteSpace(injectionPoint))
            throw new ArgumentException("An injection point is required.", nameof(injectionPoint));

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in resolvedParameters)
        {
            parameters.Add(name, value);
        }

        ResolvedParameters = new ReadOnlyDictionary<string, string>(parameters);
        PrimaryEvidence = primaryEvidence;
        InjectionPoint = injectionPoint;
    }

    public ApprovedAssociationTemplate Template { get; }

    public IReadOnlyDictionary<string, string> ResolvedParameters { get; }

    public NarrativeLedgerEntry PrimaryEvidence { get; }

    public string InjectionPoint { get; }
}

public sealed class AssociationRankerRequest
{
    internal AssociationRankerRequest(IReadOnlyList<AssociationRankerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    public IReadOnlyList<AssociationRankerCandidate> Candidates { get; }
}

public sealed class AssociationRankerCandidate
{
    internal AssociationRankerCandidate(
        string candidateToken,
        string kind,
        int evidenceSeverity,
        long evidenceAgeWorldClock,
        IReadOnlyList<string> variantTokens,
        IReadOnlyList<AssociationRankerEffectOption> effectOptions)
    {
        CandidateToken = candidateToken;
        Kind = kind;
        EvidenceSeverity = evidenceSeverity;
        EvidenceAgeWorldClock = evidenceAgeWorldClock;
        VariantTokens = Array.AsReadOnly(variantTokens.ToArray());
        EffectOptions = Array.AsReadOnly(effectOptions.ToArray());
    }

    public string CandidateToken { get; }

    public string Kind { get; }

    public int EvidenceSeverity { get; }

    public long EvidenceAgeWorldClock { get; }

    public IReadOnlyList<string> VariantTokens { get; }

    public IReadOnlyList<AssociationRankerEffectOption> EffectOptions { get; }
}

public sealed record AssociationRankerEffectOption(
    int EffectIndex,
    double? MinimumRelationshipDelta,
    double? MaximumRelationshipDelta);

internal sealed record AssociationRankerEffectSelection(
    int EffectIndex,
    double? RelationshipDelta);

internal sealed record AssociationRankerProposal(
    string CandidateToken,
    double Score,
    string VariantToken,
    IReadOnlyList<AssociationRankerEffectSelection> EffectSelections,
    int OriginalIndex);

internal sealed record AssociationDecisionSnapshot(
    AssociationDecisionRequest Request,
    IReadOnlyList<ApprovedAssociationTemplate> Templates);

internal static class AssociationDecisionSnapshotter
{
    internal static AssociationDecisionSnapshot Create(
        AssociationDecisionRequest request,
        IReadOnlyList<ApprovedAssociationTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(templates);
        AssociationDecisionRequest requestCopy = CopyRequest(request);
        ApprovedAssociationTemplate[] templateCopies = templates
            .Select(CopyTemplate)
            .ToArray();
        return new AssociationDecisionSnapshot(
            requestCopy,
            Array.AsReadOnly(templateCopies));
    }

    private static AssociationDecisionRequest CopyRequest(AssociationDecisionRequest source)
    {
        var route = new AssociationRouteContext(
            source.Route.UpcomingNodeId,
            source.Route.UpcomingLocationId,
            Array.AsReadOnly(source.Route.AllowedInjectionPoints.ToArray()));
        string[] activeFacts = source.ActiveFactIds.ToArray();
        NarrativeLedgerEntry[] ledgerEntries = source.LedgerEntries
            .Select(entry =>
            {
                LedgerEventData item = entry.Event;
                var eventCopy = new LedgerEventData(
                    item.SchemaVersion,
                    item.EntryId,
                    item.PlayerId,
                    item.Type,
                    Array.AsReadOnly(item.Actors.ToArray()),
                    item.Severity,
                    item.Chapter,
                    item.WorldClock,
                    item.SourceNodeId,
                    Array.AsReadOnly(item.FactRefs.ToArray()),
                    item.Payload.Clone());
                return new NarrativeLedgerEntry(
                    eventCopy,
                    entry.ReceivedAtUtc,
                    entry.IngestSequence);
            })
            .ToArray();
        AssociationTriggerHistory[] history = source.TriggerHistory
            .Select(item => new AssociationTriggerHistory(
                item.TemplateId,
                item.LastTriggeredWorldClock,
                item.TriggerCount))
            .ToArray();
        return new AssociationDecisionRequest(
            source.PlayerId,
            source.Chapter,
            source.WorldClock,
            route,
            Array.AsReadOnly(activeFacts),
            Array.AsReadOnly(ledgerEntries),
            Array.AsReadOnly(history),
            source.DefaultFallbackThreadId,
            source.RuntimePolicy);
    }

    private static ApprovedAssociationTemplate CopyTemplate(ApprovedAssociationTemplate source)
    {
        AssociationTemplateContract item = source.Template;
        var preconditions = new AssociationPreconditionsContract(
            Array.AsReadOnly(item.Preconditions.RequiresLedger
                .Select(requirement => new AssociationLedgerRequirementContract(
                    requirement.Type,
                    requirement.MinSeverity,
                    requirement.MaxAgeWorldClock))
                .ToArray()),
            Array.AsReadOnly(item.Preconditions.ForbidsFacts.ToArray()),
            Array.AsReadOnly(item.Preconditions.ChapterWindow.ToArray()));
        var slots = new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal);
        foreach ((string name, AssociationParameterSlotContract slot) in item.ParameterSlots)
        {
            slots.Add(name, new AssociationParameterSlotContract(slot.Source));
        }

        AssociationAllowedEffectContract[] effects = item.AllowedEffects
            .Select(effect => new AssociationAllowedEffectContract(
                effect.Op,
                effect.Field,
                effect.Range is null ? null : Array.AsReadOnly(effect.Range.ToArray()),
                effect.Value))
            .ToArray();
        var contract = new AssociationTemplateContract(
            item.SchemaVersion,
            item.TemplateId,
            item.Kind,
            item.AuthorityLevel,
            preconditions,
            new ReadOnlyDictionary<string, AssociationParameterSlotContract>(slots),
            Array.AsReadOnly(item.InjectionPoints.ToArray()),
            item.TextPolicy,
            Array.AsReadOnly(effects),
            item.MediaPolicy,
            item.FallbackThreadId,
            item.CooldownWorldClock,
            item.MaxTriggersPerPlayer,
            item.ApprovalStatus);
        return new ApprovedAssociationTemplate(source.SourcePath, contract);
    }
}

internal static class AssociationDecisionLedgerValidation
{
    private static readonly Regex EntryIdPattern = new(
        "^led\\.[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlayerIdPattern = new(
        "^p\\.[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StableIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern = new(
        "^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsPlayerId(string? value) =>
        IsMatch(value, 3, 96, PlayerIdPattern);

    internal static bool IsChapterId(string? value) =>
        IsMatch(value, 1, 64, StableIdPattern);

    internal static bool IsValidEntry(
        NarrativeLedgerEntry? entry,
        string expectedPlayerId,
        string expectedChapter)
    {
        try
        {
            LedgerEventData? value = entry?.Event;
            if (value is null ||
                entry!.IngestSequence < 1 ||
                !string.Equals(value.SchemaVersion, LedgerService.SchemaVersion, StringComparison.Ordinal) ||
                !IsMatch(value.EntryId, 5, 96, EntryIdPattern) ||
                !IsPlayerId(value.PlayerId) ||
                !string.Equals(value.PlayerId, expectedPlayerId, StringComparison.Ordinal) ||
                !LedgerEventTypes.All.Contains(value.Type) ||
                value.Actors is null ||
                value.Actors.Count is < 1 or > 8 ||
                value.Actors.Distinct(StringComparer.Ordinal).Count() != value.Actors.Count ||
                value.Actors.Any(actor => !IsMatch(actor, 1, 160, StableIdPattern)) ||
                value.Severity is < 1 or > 5 ||
                !IsChapterId(value.Chapter) ||
                !string.Equals(value.Chapter, expectedChapter, StringComparison.Ordinal) ||
                value.WorldClock is < 0 or > int.MaxValue ||
                !IsMatch(value.SourceNodeId, 1, 160, StableIdPattern) ||
                value.FactRefs is null ||
                value.FactRefs.Count > 16 ||
                value.FactRefs.Distinct(StringComparer.Ordinal).Count() != value.FactRefs.Count ||
                value.FactRefs.Any(factRef =>
                    !IsMatch(factRef, 6, 160, StableIdPattern) ||
                    !factRef.StartsWith("fact.", StringComparison.Ordinal)) ||
                !IsValidPayload(value.Type, value.Payload) ||
                JsonSerializer.SerializeToUtf8Bytes(value).Length > LedgerService.MaximumEventBytes)
            {
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            JsonException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValidPayload(string eventType, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(payload.GetRawText()) > LedgerService.MaximumPayloadBytes)
        {
            return false;
        }

        JsonProperty[] properties = payload.EnumerateObject().ToArray();
        if (properties.Length > 2 ||
            properties.Select(property => property.Name)
                .Distinct(StringComparer.Ordinal).Count() != properties.Length)
        {
            return false;
        }

        return properties.All(property => (eventType, property.Name) switch
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
        });
    }

    private static bool IsMatch(
        string? value,
        int minimumLength,
        int maximumLength,
        Regex pattern) =>
        value is not null &&
        value.Length >= minimumLength &&
        value.Length <= maximumLength &&
        pattern.IsMatch(value);

    private static bool IsToken(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 1, 64, TokenPattern);

    private static bool IsStableId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 1, 160, StableIdPattern);

    private static bool IsLedgerId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        IsMatch(value.GetString(), 5, 96, EntryIdPattern);

    private static bool IsQuantity(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal quantity) &&
        quantity == decimal.Truncate(quantity) &&
        quantity is >= 1m and <= 999_999m;
}
