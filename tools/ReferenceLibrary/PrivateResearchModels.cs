namespace Lingmai.RedMist.ReferenceLibrary;

internal sealed record PrivateResearchManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string LibraryId { get; init; } = "cx507-private-personal-research-v1";
    public string Category { get; init; } = string.Empty;
    public bool PersonalUseOnly { get; init; } = true;
    public bool ReferenceOnly { get; init; } = true;
    public bool ShipInBuild { get; init; }
    public string RightsStatus { get; init; } = "unknown-not-cleared-private-research-only";
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<PrivateResearchAsset> Assets { get; init; } = [];

    public static PrivateResearchManifest Empty(string category) => new() { Category = category };
}

internal sealed record PrivateResearchAsset
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Dimension { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string TargetLabel { get; init; } = string.Empty;
    public string SearchQuery { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SourcePageUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string LocalRelativePath { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public long ByteLength { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset DownloadedAtUtc { get; init; }
    public string CurationStatus { get; init; } = "needs_people_and_quality_review";
}

internal sealed record PrivateImageCandidate(
    string Title,
    Uri ImageUrl,
    Uri SourcePageUrl,
    int Width,
    int Height);

internal sealed record PrivateResearchQuery(
    string Dimension,
    string TargetId,
    string TargetLabel,
    string Category,
    string Search);
