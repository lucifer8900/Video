using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;

namespace Lingmai.RedMist.Generation.Associations;

public enum AssociationConsistencyRejectionReason
{
    StateUnavailable,
    UnknownEntity,
    ForbiddenFact,
    TargetDead,
    TargetUnreachable,
    EffectNotAllowed,
    EffectOutOfRange,
    CooldownActive,
    TriggerLimitReached,
    UnapprovedTextVariant,
    ModelFactConflict,
    ModelCharacterConflict,
    ModelChapterToneConflict,
    ModelInvalidResponse,
    ModelUnavailable,
    ModelTimeout,
    ModelReviewContextChanged,
}

public sealed class AssociationRejectionMetrics
{
    private readonly ConcurrentDictionary<AssociationConsistencyRejectionReason, int> _counts = new();
    private int _totalRejected;

    public int TotalRejected => Volatile.Read(ref _totalRejected);

    public int GetCount(AssociationConsistencyRejectionReason reason) =>
        _counts.TryGetValue(reason, out int count) ? count : 0;

    internal void Record(AssociationConsistencyRejectionReason reason)
    {
        _counts.AddOrUpdate(reason, 1, static (_, count) => checked(count + 1));
        Interlocked.Increment(ref _totalRejected);
    }
}

public enum AssociationNpcLifeState
{
    Unknown,
    Alive,
    Dead,
}

public sealed record AssociationNpcWorldState(
    string NpcId,
    AssociationNpcLifeState LifeState,
    string LocationId);

public sealed class AssociationWorldSnapshot
{
    public const int MaximumNpcCount = 4096;
    public const int MaximumReachableLocationCount = 4096;
    public const int MaximumActiveFactCount = AssociationDecisionLimits.MaximumActiveFactCount;

    private readonly IReadOnlyDictionary<string, AssociationNpcWorldState> _npcsById;
    private readonly IReadOnlySet<string> _reachableLocations;

    public AssociationWorldSnapshot(
        string snapshotId,
        IEnumerable<AssociationNpcWorldState> npcStates,
        IEnumerable<string> reachableLocationIds,
        IEnumerable<string>? activeFactIds = null)
    {
        if (!ApprovedAssociationContentValidation.IsId(snapshotId))
            throw new ArgumentException("A stable snapshot identifier is required.", nameof(snapshotId));
        ArgumentNullException.ThrowIfNull(npcStates);
        ArgumentNullException.ThrowIfNull(reachableLocationIds);
        activeFactIds ??= Array.Empty<string>();

        var npcs = new Dictionary<string, AssociationNpcWorldState>(StringComparer.Ordinal);
        var npcCasing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssociationNpcWorldState state in npcStates)
        {
            if (npcs.Count >= MaximumNpcCount ||
                state is null ||
                !ApprovedAssociationContentValidation.IsId(state.NpcId) ||
                !state.NpcId.StartsWith("npc.", StringComparison.Ordinal) ||
                !ApprovedAssociationContentValidation.IsId(state.LocationId) ||
                !Enum.IsDefined(state.LifeState) ||
                !npcCasing.Add(state.NpcId) ||
                !npcs.TryAdd(
                    state.NpcId,
                    new AssociationNpcWorldState(state.NpcId, state.LifeState, state.LocationId)))
            {
                throw new ArgumentException(
                    "NPC world states must be unique and contain stable identifiers.",
                    nameof(npcStates));
            }
        }

        var locations = new HashSet<string>(StringComparer.Ordinal);
        var locationCasing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string locationId in reachableLocationIds)
        {
            if (locations.Count >= MaximumReachableLocationCount ||
                !ApprovedAssociationContentValidation.IsId(locationId) ||
                !locationCasing.Add(locationId) ||
                !locations.Add(locationId))
            {
                throw new ArgumentException(
                    "Reachable locations must be unique stable identifiers.",
                    nameof(reachableLocationIds));
            }
        }

        var facts = new HashSet<string>(StringComparer.Ordinal);
        var factCasing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string factId in activeFactIds)
        {
            if (facts.Count >= MaximumActiveFactCount ||
                !ApprovedAssociationContentValidation.IsId(factId) ||
                !factId.StartsWith("fact.", StringComparison.Ordinal) ||
                !factCasing.Add(factId) ||
                !facts.Add(factId))
            {
                throw new ArgumentException(
                    "Active facts must be unique stable fact identifiers.",
                    nameof(activeFactIds));
            }
        }

        SnapshotId = snapshotId;
        NpcStates = Array.AsReadOnly(npcs.Values
            .OrderBy(state => state.NpcId, StringComparer.Ordinal)
            .ToArray());
        ReachableLocationIds = Array.AsReadOnly(locations
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray());
        ActiveFactIds = Array.AsReadOnly(facts
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray());
        _npcsById = new ReadOnlyDictionary<string, AssociationNpcWorldState>(npcs);
        _reachableLocations = locations;
    }

    public string SnapshotId { get; }

    public IReadOnlyList<AssociationNpcWorldState> NpcStates { get; }

    public IReadOnlyList<string> ReachableLocationIds { get; }

    public IReadOnlyList<string> ActiveFactIds { get; }

    internal bool TryGetNpc(string npcId, out AssociationNpcWorldState state) =>
        _npcsById.TryGetValue(npcId, out state!);

    internal bool IsReachable(string locationId) => _reachableLocations.Contains(locationId);

    internal AssociationWorldSnapshot DeepCopy() =>
        new(SnapshotId, NpcStates, ReachableLocationIds, ActiveFactIds);
}

public sealed record AssociationWorldStateQuery(
    string PlayerId,
    string ChapterId,
    long WorldClock,
    string UpcomingNodeId,
    string UpcomingLocationId);

public interface IAssociationWorldStateProvider
{
    bool TryCapture(
        AssociationWorldStateQuery query,
        out AssociationWorldSnapshot snapshot);
}

public sealed class FixedAssociationWorldStateProvider : IAssociationWorldStateProvider
{
    private readonly AssociationWorldSnapshot _snapshot;

    public FixedAssociationWorldStateProvider(AssociationWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot.DeepCopy();
    }

    public bool TryCapture(
        AssociationWorldStateQuery query,
        out AssociationWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(query);
        snapshot = _snapshot.DeepCopy();
        return true;
    }
}

public sealed record AssociationTriggerGateRequest(
    string PlayerId,
    string TemplateId,
    long WorldClock,
    long CooldownWorldClock,
    int MaxTriggers,
    long BaselineTriggerCount,
    long? BaselineLastTriggeredWorldClock);

public sealed record AssociationTriggerGateResult(
    bool Reserved,
    AssociationConsistencyRejectionReason? Reason,
    AssociationTriggerReservation? Reservation)
{
    internal static AssociationTriggerGateResult Success(
        AssociationTriggerReservation reservation) => new(true, null, reservation);
}

public sealed class AssociationTriggerReservation : IDisposable
{
    private Action<bool>? _complete;

    internal AssociationTriggerReservation(Action<bool> complete)
    {
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public void Commit()
    {
        Action<bool>? complete = Interlocked.Exchange(ref _complete, null);
        if (complete is null)
            throw new InvalidOperationException("The trigger reservation is already complete.");
        complete(true);
    }

    public void Dispose() => Interlocked.Exchange(ref _complete, null)?.Invoke(false);
}

public interface IAssociationTriggerGate
{
    AssociationTriggerGateResult TryReserve(AssociationTriggerGateRequest request);
}

/// <summary>
/// Provides a shared single-process gate for deterministic tests and local hosts.
/// Multi-worker deployments must inject a durable implementation of
/// <see cref="IAssociationTriggerGate"/> into every guard instance.
/// </summary>
public sealed class InMemoryAssociationTriggerGate : IAssociationTriggerGate
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TriggerState> _states = new(StringComparer.Ordinal);
    private long _nextReservationId;

    public AssociationTriggerGateResult TryReserve(AssociationTriggerGateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AssociationDecisionLedgerValidation.IsPlayerId(request.PlayerId) ||
            !ApprovedAssociationContentValidation.IsAssociationId(request.TemplateId) ||
            request.WorldClock < 0 ||
            request.CooldownWorldClock < 0 ||
            request.MaxTriggers <= 0 ||
            request.BaselineTriggerCount < 0 ||
            request.BaselineLastTriggeredWorldClock is < 0 ||
            request.BaselineLastTriggeredWorldClock > request.WorldClock)
        {
            throw new ArgumentException("The trigger reservation request is invalid.", nameof(request));
        }

        string key = request.PlayerId + "\n" + request.TemplateId;
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out TriggerState? state))
            {
                state = new TriggerState(
                    request.BaselineTriggerCount,
                    request.BaselineLastTriggeredWorldClock);
                _states.Add(key, state);
            }
            else
            {
                state.TriggerCount = Math.Max(
                    state.TriggerCount,
                    request.BaselineTriggerCount);
                if (request.BaselineLastTriggeredWorldClock is long baselineLast &&
                    (state.LastTriggeredWorldClock is null ||
                     baselineLast > state.LastTriggeredWorldClock.Value))
                {
                    state.LastTriggeredWorldClock = baselineLast;
                }
            }

            long effectiveTriggerCount;
            try
            {
                effectiveTriggerCount = checked(
                    state.TriggerCount + state.PendingReservations.Count);
            }
            catch (OverflowException)
            {
                effectiveTriggerCount = long.MaxValue;
            }

            if (effectiveTriggerCount >= request.MaxTriggers)
            {
                return new AssociationTriggerGateResult(
                    false,
                    AssociationConsistencyRejectionReason.TriggerLimitReached,
                    null);
            }

            long? effectiveLastTriggered = state.LastTriggeredWorldClock;
            if (state.PendingReservations.Count > 0)
            {
                long pendingLast = state.PendingReservations.Values.Max();
                if (effectiveLastTriggered is null ||
                    pendingLast > effectiveLastTriggered.Value)
                {
                    effectiveLastTriggered = pendingLast;
                }
            }

            if (effectiveLastTriggered is long lastTriggered &&
                (lastTriggered > request.WorldClock ||
                 request.WorldClock - lastTriggered < request.CooldownWorldClock))
            {
                return new AssociationTriggerGateResult(
                    false,
                    AssociationConsistencyRejectionReason.CooldownActive,
                    null);
            }

            long reservationId = checked(++_nextReservationId);
            state.PendingReservations.Add(reservationId, request.WorldClock);
            return AssociationTriggerGateResult.Success(
                new AssociationTriggerReservation(
                    committed => CompleteReservation(
                        key,
                        reservationId,
                        committed)));
        }
    }

    private void CompleteReservation(string key, long reservationId, bool committed)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out TriggerState? state) ||
                !state.PendingReservations.Remove(reservationId, out long worldClock))
            {
                return;
            }

            if (committed)
            {
                state.TriggerCount = checked(state.TriggerCount + 1);
                if (state.LastTriggeredWorldClock is null ||
                    worldClock > state.LastTriggeredWorldClock.Value)
                {
                    state.LastTriggeredWorldClock = worldClock;
                }
            }

            if (state.TriggerCount == 0 &&
                state.LastTriggeredWorldClock is null &&
                state.PendingReservations.Count == 0)
            {
                _states.Remove(key);
            }
        }
    }

    private sealed class TriggerState(
        long triggerCount,
        long? lastTriggeredWorldClock)
    {
        public long TriggerCount { get; set; } = triggerCount;

        public long? LastTriggeredWorldClock { get; set; } = lastTriggeredWorldClock;

        public Dictionary<long, long> PendingReservations { get; } = new();
    }
}

public sealed class AssociationThreadDraft
{
    public AssociationThreadDraft(
        string templateId,
        string variantToken,
        IReadOnlyDictionary<string, string> resolvedParameters,
        StoryThreadInjectionContract injection,
        IReadOnlyList<string> mediaRefs)
    {
        ArgumentNullException.ThrowIfNull(resolvedParameters);
        ArgumentNullException.ThrowIfNull(injection);
        ArgumentNullException.ThrowIfNull(mediaRefs);

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in resolvedParameters)
        {
            parameters.Add(name, value);
        }

        TemplateId = templateId;
        VariantToken = variantToken;
        ResolvedParameters = new ReadOnlyDictionary<string, string>(parameters);
        Injection = new StoryThreadInjectionContract(
            injection.NodeId,
            injection.Point,
            injection.Text,
            Array.AsReadOnly(injection.Effects
                .Select(ApprovedAssociationContentValidation.CloneEffect)
                .ToArray()));
        MediaRefs = Array.AsReadOnly(mediaRefs.ToArray());
    }

    public string TemplateId { get; }

    public string VariantToken { get; }

    public IReadOnlyDictionary<string, string> ResolvedParameters { get; }

    public StoryThreadInjectionContract Injection { get; }

    public IReadOnlyList<string> MediaRefs { get; }
}

public sealed record AssociationConsistencyInput(
    AssociationDecisionRequest Request,
    AssociationRuleCandidate Candidate,
    AssociationThreadDraft Draft,
    AssociationWorldSnapshot World);

public sealed record AssociationConsistencyResult(
    bool Passed,
    AssociationConsistencyRejectionReason? Reason)
{
    internal static AssociationConsistencyResult Success { get; } = new(true, null);
}

public sealed class AssociationConsistencyReviewOptions
{
    public AssociationConsistencyReviewOptions(bool enableModelReview = false)
    {
        EnableModelReview = enableModelReview;
    }

    public static AssociationConsistencyReviewOptions Disabled { get; } = new();

    public bool EnableModelReview { get; }
}

public sealed class AssociationConsistencyReviewRequest
{
    internal AssociationConsistencyReviewRequest(
        string chapterId,
        string templateId,
        string? targetNpcId,
        string approvedText,
        IReadOnlyList<string> activeFactIds)
    {
        SchemaVersion = "1.0.0";
        ChapterId = chapterId;
        TemplateId = templateId;
        TargetNpcId = targetNpcId;
        ApprovedText = approvedText;
        ActiveFactIds = Array.AsReadOnly(activeFactIds.ToArray());
    }

    public string SchemaVersion { get; }

    public string ChapterId { get; }

    public string TemplateId { get; }

    public string? TargetNpcId { get; }

    public string ApprovedText { get; }

    public IReadOnlyList<string> ActiveFactIds { get; }
}

public interface IAssociationConsistencyReviewer : IAssociationAuditProfileProvider
{
    Task<string?> ReviewAsync(
        AssociationConsistencyReviewRequest request,
        CancellationToken cancellationToken);
}

public sealed class MockAssociationConsistencyReviewer : IAssociationConsistencyReviewer
{
    private static readonly AssociationProviderAuditProfile Profile = new(
        "reviewer",
        "mock.association-reviewer",
        "prompt.association-reviewer.mock.v1",
        "model.association-reviewer.mock.v1");

    private readonly object _sync = new();
    private readonly Queue<string?> _responses;
    private readonly List<AssociationConsistencyReviewRequest> _requests = new();

    public MockAssociationConsistencyReviewer(IEnumerable<string?> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        _responses = new Queue<string?>(responses);
    }

    public int CallCount
    {
        get
        {
            lock (_sync) return _requests.Count;
        }
    }

    public AssociationProviderAuditProfile AuditProfile => Profile;

    public IReadOnlyList<AssociationConsistencyReviewRequest> Requests
    {
        get
        {
            lock (_sync) return Array.AsReadOnly(_requests.ToArray());
        }
    }

    public Task<string?> ReviewAsync(
        AssociationConsistencyReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _requests.Add(request);
            return Task.FromResult(_responses.Count == 0 ? null : _responses.Dequeue());
        }
    }
}

public sealed record AssociationConsistencyReviewResponse(
    string SchemaVersion,
    string Decision,
    string ReasonCode);

public static class AssociationConsistencyReviewWireProtocol
{
    public const int MaximumResponseBytes = 4 * 1024;
    public const int MaximumJsonDepth = 8;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth,
    };

    public static bool TryParse(
        string? rawJson,
        out AssociationConsistencyReviewResponse? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(rawJson) ||
            Encoding.UTF8.GetByteCount(rawJson) > MaximumResponseBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson, DocumentOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(root, "schemaVersion", "decision", "reasonCode") ||
                !TryReadString(root, "schemaVersion", out string? schemaVersion) ||
                !TryReadString(root, "decision", out string? decision) ||
                !TryReadString(root, "reasonCode", out string? reasonCode) ||
                !string.Equals(schemaVersion, "1.0.0", StringComparison.Ordinal))
            {
                return false;
            }

            bool valid = (decision, reasonCode) switch
            {
                (AssociationConsistencyReviewDecisions.Pass,
                    AssociationConsistencyReviewReasonCodes.None) => true,
                (AssociationConsistencyReviewDecisions.Reject,
                    AssociationConsistencyReviewReasonCodes.FactConflict) => true,
                (AssociationConsistencyReviewDecisions.Reject,
                    AssociationConsistencyReviewReasonCodes.CharacterConflict) => true,
                (AssociationConsistencyReviewDecisions.Reject,
                    AssociationConsistencyReviewReasonCodes.ChapterToneConflict) => true,
                _ => false,
            };
            if (!valid) return false;

            response = new AssociationConsistencyReviewResponse(
                schemaVersion!,
                decision!,
                reasonCode!);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool HasExactProperties(JsonElement element, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) ||
                !exact.Add(property.Name) ||
                !folded.Add(property.Name))
            {
                return false;
            }
        }

        return exact.Count == names.Length && names.All(exact.Contains);
    }
}
