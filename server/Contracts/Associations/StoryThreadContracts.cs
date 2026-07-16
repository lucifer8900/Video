using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Associations;

public static class StoryThreadInjectionPoints
{
    public const string NodeIntro = "node_intro";
    public const string TravelEvent = "travel_event";
    public const string NpcMention = "npc_mention";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        NodeIntro,
        TravelEvent,
        NpcMention,
    };
}

public static class StoryThreadEffectOperations
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
public sealed record StoryThreadContract(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] string ThreadId,
    [property: JsonRequired] string TemplateId,
    [property: JsonRequired] string PlayerId,
    [property: JsonRequired] IReadOnlyDictionary<string, string> ResolvedParams,
    [property: JsonRequired] IReadOnlyList<StoryThreadInjectionContract> Injections,
    [property: JsonRequired] IReadOnlyList<string> MediaRefs,
    [property: JsonRequired] bool ExpiresAtChapterEnd,
    [property: JsonRequired] string AuditRef,
    [property: JsonRequired] bool FallbackUsed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoryThreadInjectionContract(
    [property: JsonRequired] string NodeId,
    [property: JsonRequired] string Point,
    [property: JsonRequired] string Text,
    [property: JsonRequired] IReadOnlyList<StoryThreadEffectContract> Effects);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoryThreadEffectContract(
    [property: JsonRequired] string Op,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetRef,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Delta,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value);
