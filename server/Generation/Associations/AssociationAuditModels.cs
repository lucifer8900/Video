using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Associations;

public sealed record AssociationProviderAuditProfile
{
    private static readonly Regex StableId = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AssociationProviderAuditProfile(
        string role,
        string providerKey,
        string promptVersion,
        string modelSnapshot)
    {
        if (role is not ("ranker" or "reviewer") ||
            !IsStable(providerKey) ||
            !IsStable(promptVersion) ||
            !IsStable(modelSnapshot))
        {
            throw new ArgumentException("Association provider audit metadata is invalid.");
        }

        Role = role;
        ProviderKey = providerKey;
        PromptVersion = promptVersion;
        ModelSnapshot = modelSnapshot;
    }

    public string Role { get; }

    public string ProviderKey { get; }

    public string PromptVersion { get; }

    public string ModelSnapshot { get; }

    private static bool IsStable(string? value) =>
        value is not null && StableId.IsMatch(value);
}

public interface IAssociationAuditProfileProvider
{
    AssociationProviderAuditProfile AuditProfile { get; }
}

public interface IAssociationDecisionAuditRepository
{
    ValueTask StoreAsync(
        AssociationDecisionAuditContract audit,
        CancellationToken cancellationToken);

    ValueTask<AssociationDecisionAuditContract?> GetAsync(
        string auditRef,
        CancellationToken cancellationToken);

    ValueTask<int> DeletePlayerAsync(
        string playerId,
        CancellationToken cancellationToken);
}

public sealed class AssociationDecisionAuditException : InvalidOperationException
{
    internal AssociationDecisionAuditException(Exception innerException)
        : base("The association thread audit could not be persisted.", innerException)
    {
    }
}

public sealed class AssociationDecisionAuditConflictException : InvalidOperationException
{
    public AssociationDecisionAuditConflictException(string auditRef)
        : base($"Association audit '{auditRef}' conflicts with an existing record.")
    {
        AuditRef = auditRef;
    }

    public string AuditRef { get; }
}

public sealed class AssociationDecisionAuditSuppressedException : InvalidOperationException
{
    internal AssociationDecisionAuditSuppressedException()
        : base("Association audit storage is suppressed for a deleted player.")
    {
    }
}

public static class AssociationAuditHash
{
    private static readonly Regex PlayerId = new(
        "^p\\.[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ComputePlayerRefHash(string playerId)
    {
        if (string.IsNullOrEmpty(playerId) ||
            playerId.Length > 96 ||
            !PlayerId.IsMatch(playerId))
        {
            throw new ArgumentException("A canonical player identifier is required.", nameof(playerId));
        }

        return ComputeSha256("player-ref:v1:" + playerId);
    }

    internal static string ComputeInputHash(
        AssociationDecisionRequest request,
        IReadOnlyList<ApprovedAssociationTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(templates);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("playerId", request.PlayerId);
            writer.WriteString("chapter", request.Chapter);
            writer.WriteNumber("worldClock", request.WorldClock);
            writer.WriteBoolean(
                "allowRuntimeGeneration",
                request.EffectiveRuntimePolicy.AllowRuntimeGeneration);
            writer.WritePropertyName("route");
            writer.WriteStartObject();
            writer.WriteString("upcomingNodeId", request.Route.UpcomingNodeId);
            writer.WriteString("upcomingLocationId", request.Route.UpcomingLocationId);
            WriteStrings(writer, "allowedInjectionPoints", request.Route.AllowedInjectionPoints);
            writer.WriteEndObject();
            WriteStrings(writer, "activeFactIds", request.ActiveFactIds);
            writer.WritePropertyName("ledgerEntries");
            writer.WriteStartArray();
            foreach (NarrativeLedgerEntry entry in request.LedgerEntries
                         .OrderBy(item => item.IngestSequence)
                         .ThenBy(item => item.Event.EntryId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteNumber("ingestSequence", entry.IngestSequence);
                writer.WriteString("entryId", entry.Event.EntryId);
                writer.WriteString("type", entry.Event.Type);
                writer.WriteNumber("severity", entry.Event.Severity);
                writer.WriteNumber("worldClock", entry.Event.WorldClock);
                WriteStrings(writer, "actors", entry.Event.Actors);
                WriteStrings(writer, "factRefs", entry.Event.FactRefs);
                writer.WritePropertyName("payload");
                WriteCanonicalElement(writer, entry.Event.Payload);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("triggerHistory");
            writer.WriteStartArray();
            foreach (AssociationTriggerHistory item in request.TriggerHistory
                         .OrderBy(value => value.TemplateId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("templateId", item.TemplateId);
                writer.WriteNumber("lastTriggeredWorldClock", item.LastTriggeredWorldClock);
                writer.WriteNumber("triggerCount", item.TriggerCount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("defaultFallbackThreadId", request.DefaultFallbackThreadId);
            writer.WritePropertyName("templateIds");
            writer.WriteStartArray();
            foreach (string templateId in templates
                         .Select(item => item.Template.TemplateId)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                writer.WriteStringValue(templateId);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeSha256(stream.ToArray());
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (string value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject()
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                    WriteCanonicalElement(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Unsupported audit input JSON value.");
        }
    }

    private static string ComputeSha256(string value) =>
        ComputeSha256(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256(byte[] value)
    {
        byte[] digest = SHA256.HashData(value);
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }
}

public sealed class InMemoryAssociationDecisionAuditRepository
    : IAssociationDecisionAuditRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredAudit> _byAuditRef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _auditRefByThreadId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tombstonedPlayerHashes = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_sync) return _byAuditRef.Count;
        }
    }

    public ValueTask StoreAsync(
        AssociationDecisionAuditContract audit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssociationDecisionAuditContract frozen = AssociationAuditValidation.Freeze(audit);
        string fingerprint = AssociationAuditValidation.Fingerprint(frozen);
        lock (_sync)
        {
            if (_tombstonedPlayerHashes.Contains(frozen.PlayerRefHash))
                throw new AssociationDecisionAuditSuppressedException();

            if (_byAuditRef.TryGetValue(frozen.AuditRef, out StoredAudit? existing))
            {
                if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return ValueTask.CompletedTask;
                throw new AssociationDecisionAuditConflictException(frozen.AuditRef);
            }

            if (_auditRefByThreadId.TryGetValue(frozen.ThreadId, out string? existingAuditRef))
                throw new AssociationDecisionAuditConflictException(existingAuditRef);
            _byAuditRef.Add(frozen.AuditRef, new StoredAudit(frozen, fingerprint));
            _auditRefByThreadId.Add(frozen.ThreadId, frozen.AuditRef);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<AssociationDecisionAuditContract?> GetAsync(
        string auditRef,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AssociationAuditValidation.IsAuditRef(auditRef))
            throw new ArgumentException("A valid association audit reference is required.", nameof(auditRef));
        lock (_sync)
        {
            return ValueTask.FromResult(
                _byAuditRef.TryGetValue(auditRef, out StoredAudit? stored)
                    ? AssociationAuditValidation.Freeze(stored.Audit)
                    : null);
        }
    }

    public ValueTask<int> DeletePlayerAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string hash = AssociationAuditHash.ComputePlayerRefHash(playerId);
        lock (_sync)
        {
            _tombstonedPlayerHashes.Add(hash);
            string[] matches = _byAuditRef
                .Where(item => string.Equals(
                    item.Value.Audit.PlayerRefHash,
                    hash,
                    StringComparison.Ordinal))
                .Select(item => item.Key)
                .ToArray();
            foreach (string auditRef in matches)
            {
                StoredAudit stored = _byAuditRef[auditRef];
                _byAuditRef.Remove(auditRef);
                _auditRefByThreadId.Remove(stored.Audit.ThreadId);
            }
            return ValueTask.FromResult(matches.Length);
        }
    }

    private sealed record StoredAudit(
        AssociationDecisionAuditContract Audit,
        string Fingerprint);
}

internal static class AssociationAuditValidation
{
    private const int MaximumAuditBytes = 64 * 1024;
    private static readonly HashSet<string> GuardStages = new(StringComparer.Ordinal)
    {
        "prefilter",
        "selection",
        "deterministic",
        "review",
        "signing",
        "trigger_reservation",
        "fallback",
    };
    private static readonly HashSet<string> ReasonCodes = new(StringComparer.Ordinal)
    {
        "none",
        "disabled",
        "no_candidates",
        "approved_fallback",
        "ranker_invalid_response",
        "ranker_unavailable",
        "ranker_timeout",
        "no_valid_proposal",
        "guard_rejected",
        "reviewer_rejected",
        "deadline_exceeded",
        "state_unavailable",
        "unknown_entity",
        "forbidden_fact",
        "target_dead",
        "target_unreachable",
        "effect_not_allowed",
        "effect_out_of_range",
        "cooldown_active",
        "trigger_limit_reached",
        "unapproved_text_variant",
        "model_fact_conflict",
        "model_character_conflict",
        "model_chapter_tone_conflict",
        "model_invalid_response",
        "model_unavailable",
        "model_timeout",
        "model_review_context_changed",
    };
    private static readonly Regex StableId = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new(
        "^sha256:[a-f0-9]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static AssociationDecisionAuditContract Freeze(
        AssociationDecisionAuditContract audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (!string.Equals(audit.SchemaVersion, "1.0.0", StringComparison.Ordinal) ||
            !IsAuditRef(audit.AuditRef) ||
            !IsPrefixed(audit.ThreadId, "thr.") ||
            !Hash.IsMatch(audit.InputHash ?? string.Empty) ||
            !Hash.IsMatch(audit.PlayerRefHash ?? string.Empty) ||
            !IsPrefixed(audit.ChapterId, "chapter.") ||
            !IsPrefixed(audit.TemplateId, "assoc.") ||
            audit.ResolvedParams is null ||
            audit.ResolvedParams.Count > 16 ||
            audit.Providers is null ||
            audit.Providers.Count is < 1 or > 2 ||
            audit.GuardResults is null ||
            audit.GuardResults.Count is < 1 or > 128 ||
            audit.CandidateCount is < 0 or > AssociationRankerWireProtocol.MaximumProposalCount ||
            audit.LatencyMilliseconds is < 0 or > 600_000 ||
            (audit.FallbackUsed && !ReasonCodes.Contains(audit.OutcomeReason ?? string.Empty)) ||
            (!audit.FallbackUsed && audit.OutcomeReason is not null))
        {
            throw new ArgumentException("The association decision audit is invalid.", nameof(audit));
        }

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in audit.ResolvedParams)
        {
            if (name.Length is < 1 or > 64 ||
                !char.IsAsciiLetter(name[0]) ||
                name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                !IsStable(value) ||
                !parameters.TryAdd(name, value))
                throw new ArgumentException("The association audit parameters are invalid.", nameof(audit));
        }

        AssociationProviderAuditContract[] providers = audit.Providers
            .Select(provider =>
            {
                if (provider is null ||
                    provider.Role is not ("ranker" or "reviewer") ||
                    !IsStable(provider.ProviderKey) ||
                    !IsStable(provider.PromptVersion) ||
                    !IsStable(provider.ModelSnapshot))
                {
                    throw new ArgumentException("The association provider audit is invalid.", nameof(audit));
                }
                return new AssociationProviderAuditContract(
                    provider.Role,
                    provider.ProviderKey,
                    provider.PromptVersion,
                    provider.ModelSnapshot,
                    provider.Invoked);
            })
            .ToArray();
        if (providers.Select(item => item.Role).Distinct(StringComparer.Ordinal).Count() != providers.Length ||
            providers.Count(item => string.Equals(item.Role, "ranker", StringComparison.Ordinal)) != 1)
            throw new ArgumentException("Association provider roles must be unique.", nameof(audit));

        AssociationGuardResultAuditContract[] guards = audit.GuardResults
            .Select(result =>
            {
                if (result is null ||
                    !GuardStages.Contains(result.Stage) ||
                    result.Decision is not ("passed" or "rejected" or "not_run") ||
                    !ReasonCodes.Contains(result.ReasonCode) ||
                    (result.WorldSnapshotId is not null && !IsStable(result.WorldSnapshotId)))
                {
                    throw new ArgumentException("The association guard audit is invalid.", nameof(audit));
                }
                return new AssociationGuardResultAuditContract(
                    result.Stage,
                    result.Decision,
                    result.ReasonCode,
                    result.WorldSnapshotId);
            })
            .ToArray();

        var frozen = new AssociationDecisionAuditContract(
            audit.SchemaVersion,
            audit.AuditRef,
            audit.ThreadId,
            audit.InputHash!,
            audit.PlayerRefHash!,
            audit.ChapterId,
            audit.TemplateId,
            new ReadOnlyDictionary<string, string>(parameters),
            Array.AsReadOnly(providers),
            Array.AsReadOnly(guards),
            audit.CandidateCount,
            audit.FallbackUsed,
            audit.OutcomeReason,
            audit.LatencyMilliseconds,
            audit.CreatedAtUtc.ToUniversalTime());
        if (JsonSerializer.SerializeToUtf8Bytes(frozen).Length > MaximumAuditBytes)
            throw new ArgumentException("The association audit exceeds its size limit.", nameof(audit));
        return frozen;
    }

    internal static bool IsAuditRef(string? value) => IsPrefixed(value, "genjob.");

    internal static string Fingerprint(AssociationDecisionAuditContract audit)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Freeze(audit));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool IsPrefixed(string? value, string prefix) =>
        IsStable(value) && value!.Length > prefix.Length && value.StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsStable(string? value) =>
        value is not null && StableId.IsMatch(value);
}

internal sealed class AssociationDecisionTrace
{
    private const int MaximumAuditedGuardResults = 128;
    private readonly List<AssociationGuardResultAuditContract> _guardResults = new();
    private readonly List<AssociationDecisionRejectionObservation> _rejections = new();

    internal int CandidateCount { get; set; }

    internal bool RankerInvoked { get; set; }

    internal bool ReviewerInvoked { get; set; }

    internal string? OutcomeReason { get; private set; }

    internal IReadOnlyList<AssociationGuardResultAuditContract> GuardResults => _guardResults;

    internal IReadOnlyList<AssociationDecisionRejectionObservation> Rejections => _rejections;

    internal void SetFallback(string reason)
    {
        if (!IsToken(reason)) throw new ArgumentException("A stable fallback reason is required.", nameof(reason));
        OutcomeReason ??= reason;
    }

    internal void Record(
        string stage,
        string decision,
        string reason,
        string? worldSnapshotId = null)
    {
        var result = new AssociationGuardResultAuditContract(
            stage,
            decision,
            reason,
            worldSnapshotId);
        if (_guardResults.Count < MaximumAuditedGuardResults &&
            !_guardResults.Contains(result))
        {
            _guardResults.Add(result);
        }
        if (decision == "rejected")
            _rejections.Add(new AssociationDecisionRejectionObservation(stage, reason));
    }

    internal void RecordRejection(
        string stage,
        AssociationConsistencyRejectionReason reason,
        string? worldSnapshotId = null) =>
        Record(stage, "rejected", ToSnakeCase(reason.ToString()), worldSnapshotId);

    internal void EnsureFinalResult(bool fallbackUsed)
    {
        if (_guardResults.Count == 0)
        {
            Record(
                fallbackUsed ? "fallback" : "selection",
                fallbackUsed ? "passed" : "passed",
                fallbackUsed ? "approved_fallback" : "none");
        }
        else if (fallbackUsed)
        {
            Record("fallback", "passed", "approved_fallback");
        }
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 64 && value.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= '0' && character <= '9') ||
            character == '_');
}

internal static class AssociationDecisionAuditFactory
{
    internal static AssociationDecisionAuditContract Create(
        StoryThreadContract thread,
        AssociationDecisionRequest request,
        string inputHash,
        AssociationDecisionTrace trace,
        AssociationProviderAuditProfile ranker,
        AssociationProviderAuditProfile? reviewer,
        bool reviewerEnabled,
        TimeSpan elapsed,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trace);
        trace.EnsureFinalResult(thread.FallbackUsed);
        var providers = new List<AssociationProviderAuditContract>(2)
        {
            new(
                ranker.Role,
                ranker.ProviderKey,
                ranker.PromptVersion,
                ranker.ModelSnapshot,
                trace.RankerInvoked),
        };
        if (reviewerEnabled && reviewer is not null)
        {
            providers.Add(new AssociationProviderAuditContract(
                reviewer.Role,
                reviewer.ProviderKey,
                reviewer.PromptVersion,
                reviewer.ModelSnapshot,
                trace.ReviewerInvoked));
        }

        long elapsedMilliseconds = elapsed <= TimeSpan.Zero
            ? 0
            : Math.Min(600_000, checked((long)Math.Ceiling(elapsed.TotalMilliseconds)));
        return AssociationAuditValidation.Freeze(new AssociationDecisionAuditContract(
            "1.0.0",
            thread.AuditRef,
            thread.ThreadId,
            inputHash,
            AssociationAuditHash.ComputePlayerRefHash(request.PlayerId),
            request.Chapter,
            thread.TemplateId,
            thread.ResolvedParams,
            providers,
            trace.GuardResults,
            trace.CandidateCount,
            thread.FallbackUsed,
            thread.FallbackUsed ? trace.OutcomeReason ?? "approved_fallback" : null,
            elapsedMilliseconds,
            createdAtUtc));
    }
}
