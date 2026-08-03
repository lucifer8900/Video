using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Associations;

public static class AssociationConsistencyReviewDecisions
{
    public const string Pass = "pass";
    public const string Reject = "reject";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pass,
        Reject,
    };
}

public static class AssociationConsistencyReviewReasonCodes
{
    public const string None = "none";
    public const string FactConflict = "fact_conflict";
    public const string CharacterConflict = "character_conflict";
    public const string ChapterToneConflict = "chapter_tone_conflict";

    public static readonly IReadOnlySet<string> RejectionReasons =
        new HashSet<string>(StringComparer.Ordinal)
        {
            FactConflict,
            CharacterConflict,
            ChapterToneConflict,
        };

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        None,
        FactConflict,
        CharacterConflict,
        ChapterToneConflict,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationConsistencyReviewRequestContract(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] string ChapterId,
    [property: JsonRequired] string TemplateId,
    [property: JsonRequired] string? TargetNpcId,
    [property: JsonRequired] IReadOnlyList<string> ActiveFactIds,
    [property: JsonRequired] string ApprovedText);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssociationConsistencyReviewContract(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] string Decision,
    [property: JsonRequired] string ReasonCode);
