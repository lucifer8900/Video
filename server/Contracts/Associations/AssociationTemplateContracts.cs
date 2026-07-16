using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Associations;

public static class AssociationKinds
{
    public const string Callback = "callback";
    public const string Foreshadow = "foreshadow";
    public const string Consequence = "consequence";
    public const string Encounter = "encounter";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Callback,
        Foreshadow,
        Consequence,
        Encounter,
    };
}

public static class AssociationMediaPolicies
{
    public const string ReuseOnly = "reuse_only";
    public const string AllowRuntimeGeneration = "allow_runtime_generation";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ReuseOnly,
        AllowRuntimeGeneration,
    };
}

public static class AssociationEffectOperations
{
    public const string Relationship = "relationship";
    public const string Clue = "clue";
    public const string BranchUnlock = "branch_unlock";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Relationship,
        Clue,
        BranchUnlock,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationTemplateContract(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] string TemplateId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string AuthorityLevel,
    [property: JsonRequired] AssociationPreconditionsContract Preconditions,
    [property: JsonRequired] IReadOnlyDictionary<string, AssociationParameterSlotContract> ParameterSlots,
    [property: JsonRequired] IReadOnlyList<string> InjectionPoints,
    [property: JsonRequired] string TextPolicy,
    [property: JsonRequired] IReadOnlyList<AssociationAllowedEffectContract> AllowedEffects,
    [property: JsonRequired] string MediaPolicy,
    [property: JsonRequired] string FallbackThreadId,
    [property: JsonRequired] long CooldownWorldClock,
    [property: JsonRequired] int MaxTriggersPerPlayer,
    [property: JsonRequired] string ApprovalStatus);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationPreconditionsContract(
    [property: JsonRequired] IReadOnlyList<AssociationLedgerRequirementContract> RequiresLedger,
    [property: JsonRequired] IReadOnlyList<string> ForbidsFacts,
    [property: JsonRequired] IReadOnlyList<string> ChapterWindow);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationLedgerRequirementContract(
    [property: JsonRequired] string Type,
    [property: JsonRequired] int MinSeverity,
    [property: JsonRequired] long MaxAgeWorldClock);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationParameterSlotContract(
    [property: JsonRequired] string Source);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationAllowedEffectContract(
    [property: JsonRequired] string Op,
    string? Field,
    IReadOnlyList<double>? Range,
    string? Value);
