using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Associations;

public sealed class AssociationEntityRegistry
{
    private static readonly Regex StableIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _factIds;
    private readonly HashSet<string> _chapterIds;
    private readonly HashSet<string> _clueIds;
    private readonly HashSet<string> _branchIds;

    public AssociationEntityRegistry(
        IEnumerable<string> factIds,
        IEnumerable<string> chapterIds,
        IEnumerable<string> clueIds,
        IEnumerable<string> branchIds)
    {
        _factIds = CreateBucket(factIds, nameof(factIds));
        _chapterIds = CreateBucket(chapterIds, nameof(chapterIds));
        _clueIds = CreateBucket(clueIds, nameof(clueIds));
        _branchIds = CreateBucket(branchIds, nameof(branchIds));
    }

    public bool ContainsFact(string value) => _factIds.Contains(value);

    public bool ContainsChapter(string value) => _chapterIds.Contains(value);

    public bool ContainsClue(string value) => _clueIds.Contains(value);

    public bool ContainsBranch(string value) => _branchIds.Contains(value);

    private static HashSet<string> CreateBucket(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            if (value is null ||
                value.Length is < 1 or > 160 ||
                !StableIdPattern.IsMatch(value))
            {
                throw new ArgumentException("Registry entries must be stable identifiers.", parameterName);
            }

            if (!casing.Add(value) || !result.Add(value))
            {
                throw new ArgumentException(
                    "Registry entries must not contain case-fold collisions.",
                    parameterName);
            }
        }

        return result;
    }
}

internal sealed record AssociationFallbackThreadValidationFixture(
    string ThreadId,
    string ApprovalStatus);

public sealed class AssociationFallbackThreadRegistry
{
    private static readonly IReadOnlySet<string> ApprovalStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "draft",
        "needs_review",
        "approved",
    };

    private readonly IReadOnlyDictionary<string, string> _approvalByThreadId;

    private AssociationFallbackThreadRegistry(IReadOnlyDictionary<string, string> approvalByThreadId)
    {
        _approvalByThreadId = approvalByThreadId;
    }

    public static AssociationFallbackThreadRegistry Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    internal static AssociationFallbackThreadRegistry CreateForValidationTests(
        IEnumerable<AssociationFallbackThreadValidationFixture> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssociationFallbackThreadValidationFixture registration in registrations)
        {
            if (registration is null ||
                string.IsNullOrWhiteSpace(registration.ThreadId) ||
                registration.ThreadId.Length > 160 ||
                !registration.ThreadId.StartsWith("assoc.", StringComparison.Ordinal) ||
                !StableFallbackIdPattern.IsMatch(registration.ThreadId) ||
                !ApprovalStatuses.Contains(registration.ApprovalStatus) ||
                !casing.Add(registration.ThreadId) ||
                !result.TryAdd(registration.ThreadId, registration.ApprovalStatus))
            {
                throw new ArgumentException(
                    "Fallback registrations must have unique identifiers and valid approval states.",
                    nameof(registrations));
            }
        }

        return new AssociationFallbackThreadRegistry(result);
    }

    internal bool TryGetApprovalStatus(string threadId, out string approvalStatus) =>
        _approvalByThreadId.TryGetValue(threadId, out approvalStatus!);

    private static readonly Regex StableFallbackIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
