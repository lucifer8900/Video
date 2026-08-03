using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Associations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationDecisionAuditContract(
    [property: JsonPropertyName("schemaVersion"), JsonRequired] string SchemaVersion,
    [property: JsonPropertyName("auditRef"), JsonRequired] string AuditRef,
    [property: JsonPropertyName("threadId"), JsonRequired] string ThreadId,
    [property: JsonPropertyName("inputHash"), JsonRequired] string InputHash,
    [property: JsonPropertyName("playerRefHash"), JsonRequired] string PlayerRefHash,
    [property: JsonPropertyName("chapterId"), JsonRequired] string ChapterId,
    [property: JsonPropertyName("templateId"), JsonRequired] string TemplateId,
    [property: JsonPropertyName("resolvedParams"), JsonRequired]
        IReadOnlyDictionary<string, string> ResolvedParams,
    [property: JsonPropertyName("providers"), JsonRequired]
        IReadOnlyList<AssociationProviderAuditContract> Providers,
    [property: JsonPropertyName("guardResults"), JsonRequired]
        IReadOnlyList<AssociationGuardResultAuditContract> GuardResults,
    [property: JsonPropertyName("candidateCount"), JsonRequired] int CandidateCount,
    [property: JsonPropertyName("fallbackUsed"), JsonRequired] bool FallbackUsed,
    [property: JsonPropertyName("outcomeReason"), JsonRequired] string? OutcomeReason,
    [property: JsonPropertyName("latencyMilliseconds"), JsonRequired] long LatencyMilliseconds,
    [property: JsonPropertyName("createdAtUtc"), JsonRequired] DateTimeOffset CreatedAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationProviderAuditContract(
    [property: JsonPropertyName("role"), JsonRequired] string Role,
    [property: JsonPropertyName("providerKey"), JsonRequired] string ProviderKey,
    [property: JsonPropertyName("promptVersion"), JsonRequired] string PromptVersion,
    [property: JsonPropertyName("modelSnapshot"), JsonRequired] string ModelSnapshot,
    [property: JsonPropertyName("invoked"), JsonRequired] bool Invoked);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationGuardResultAuditContract(
    [property: JsonPropertyName("stage"), JsonRequired] string Stage,
    [property: JsonPropertyName("decision"), JsonRequired] string Decision,
    [property: JsonPropertyName("reasonCode"), JsonRequired] string ReasonCode,
    [property: JsonPropertyName("worldSnapshotId"),
               JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? WorldSnapshotId);
