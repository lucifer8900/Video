using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ReferenceLibrary.Tests")]

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ReferenceJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

internal sealed record AcquisitionPlan
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string DiscoveryEndpoint { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedLicenses { get; init; } = [];
    public bool CommercialUseRequired { get; init; }
    public bool ModificationRequired { get; init; }
    public int MinimumTotalAssets { get; init; }
    public int PageSize { get; init; }
    public int MaxPagesPerCategory { get; init; }
    public long MaxDownloadBytes { get; init; }
    public int MinimumWidth { get; init; }
    public int MinimumHeight { get; init; }
    public IReadOnlyList<AcquisitionCategory> Categories { get; init; } = [];
}

internal sealed record AcquisitionCategory
{
    public string Id { get; init; } = string.Empty;
    public string QueryMatchField { get; init; } = string.Empty;
    public bool RequireOnePerQuery { get; init; }
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<string> Queries { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> QueryRequiredAnyTerms { get; init; } = [];
    public IReadOnlyList<string> RequiredAnyTerms { get; init; } = [];
    public IReadOnlyList<string> ExcludedTerms { get; init; } = [];
    public bool RequireChinaEvidence { get; init; }
    public IReadOnlyList<string> ChinaEvidenceTerms { get; init; } = [];
    public int MinimumAssets { get; init; }
    public IReadOnlyList<string> ReferenceRoles { get; init; } = [];
}

internal sealed record ChinaExpansionPlan
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public bool ReferenceOnly { get; init; }
    public bool ShipInBuild { get; init; }
    public IReadOnlyList<string> AllowedLicenses { get; init; } = [];
    public bool CommercialUseRequired { get; init; }
    public bool ModificationRequired { get; init; }
    public bool RejectShareAlike { get; init; }
    public bool ArchitectureMustExcludePeople { get; init; }
    public IReadOnlyList<string> ArchitecturePersonExclusionTerms { get; init; } = [];
    public IReadOnlyList<string> ArchitectureSubjectExclusionTerms { get; init; } = [];
    public int MinimumTotalAssets { get; init; }
    public IReadOnlyDictionary<string, int> MinimumAssetsPerCategory { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public string DiscoveryEndpoint { get; init; } = string.Empty;
    public string INaturalistEndpoint { get; init; } = string.Empty;
    public int INaturalistPlaceId { get; init; }
    public string MetMuseumEndpoint { get; init; } = string.Empty;
    public int MetMuseumDepartmentId { get; init; }
    public string ClevelandMuseumEndpoint { get; init; } = string.Empty;
    public string ArtInstituteChicagoEndpoint { get; init; } = string.Empty;
    public int ThumbnailWidth { get; init; }
    public int PageSize { get; init; }
    public int MaxPagesPerQuery { get; init; }
    public long MaxDownloadBytes { get; init; }
    public int MinimumWidth { get; init; }
    public int MinimumHeight { get; init; }
    public int RequestDelayMilliseconds { get; init; }
    public IReadOnlyList<string> ExcludedTerms { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SubjectEvidenceTerms { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    public ExpansionCoverage Coverage { get; init; } = new();
}

internal sealed record ExpansionCoverage
{
    public IReadOnlyList<ExpansionTarget> ProvinceRegions { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> HistoricalCapitals { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> HistoricalPeriods { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> GardenTypes { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> Landforms { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> WeatherPhenomena { get; init; } = [];
    public IReadOnlyList<ExpansionTarget> CategorySubjects { get; init; } = [];
}

internal sealed record ExpansionTarget
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int MinimumAssets { get; init; }
    public int? MinimumArchitectureAssets { get; init; }
    public int? MinimumNaturalAssets { get; init; }
    public IReadOnlyList<string> EvidenceTerms { get; init; } = [];
    public bool RequireEvidence { get; init; }
    public IReadOnlyList<ExpansionQuery> Queries { get; init; } = [];
}

internal sealed record ExpansionQuery
{
    public string Id { get; init; } = string.Empty;
    public string Search { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceTerms { get; init; } = [];
    public string? INaturalistTaxon { get; init; }
    public int? INaturalistTaxonId { get; init; }
    public string? MetMuseumQuery { get; init; }
    public string? ArtInstituteChicagoQuery { get; init; }
    public int? MinimumAssets { get; init; }
}

internal sealed record ReferenceCatalog
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public string? CoveragePlanId { get; init; }
    public bool ReferenceOnly { get; init; }
    public bool ShipInBuild { get; init; }
    public int MinimumAssetsPerCategory { get; init; }
    public IReadOnlyList<string> AllowedLicenses { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<ReferenceAsset> Assets { get; init; } = [];
}

internal sealed record ReferenceAsset
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Creator { get; init; } = string.Empty;
    public string? CreatorUrl { get; init; }
    public string Source { get; init; } = string.Empty;
    public string SourcePageUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string LicenseVersion { get; init; } = string.Empty;
    public string LicenseUrl { get; init; } = string.Empty;
    public bool CommercialUseAllowed { get; init; }
    public bool ModificationsAllowed { get; init; }
    public bool ShareAlikeRequired { get; init; }
    public bool ReferenceOnly { get; init; }
    public bool ShipInBuild { get; init; }
    public bool IdentityReuse { get; init; }
    public IReadOnlyList<string> ReferenceRoles { get; init; } = [];
    public string LocalRelativePath { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public long ByteLength { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset DownloadedAtUtc { get; init; }
    public string CurationStatus { get; init; } = "needs_review";
    public IReadOnlyList<string> CoverageTargetIds { get; init; } = [];
    public ReferenceTaxonomy Taxonomy { get; init; } = new();
}

internal sealed record ReferenceTaxonomy
{
    public string Authenticity { get; init; } = "unknown_needs_review";
    public string PeoplePresence { get; init; } = "unknown_needs_review";
    public IReadOnlyList<string> ProvinceRegionIds { get; init; } = [];
    public IReadOnlyList<string> LocationLabels { get; init; } = [];
    public IReadOnlyList<string> HistoricalCapitalIds { get; init; } = [];
    public IReadOnlyList<string> HistoricalPeriodIds { get; init; } = [];
    public IReadOnlyList<string> ArchitectureTypeIds { get; init; } = [];
    public IReadOnlyList<string> GardenTypeIds { get; init; } = [];
    public IReadOnlyList<string> LandformIds { get; init; } = [];
    public IReadOnlyList<string> WeatherPhenomenonIds { get; init; } = [];
    public IReadOnlyList<string> Seasons { get; init; } = [];
    public IReadOnlyList<string> TimesOfDay { get; init; } = [];
}

internal sealed record ArchitectureCurationDecision
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string CatalogLibraryId { get; init; } = string.Empty;
    public string Reviewer { get; init; } = string.Empty;
    public DateTimeOffset ReviewedAtUtc { get; init; }
    public string Policy { get; init; } = string.Empty;
    public int ExpectedArchitectureAssets { get; init; }
    public string ArchitectureOrderSha256 { get; init; } = string.Empty;
    public bool QuarantineUnlisted { get; init; }
    public IReadOnlyList<int> ConfirmedAbsentIndices { get; init; } = [];
    public IReadOnlyList<ReviewedContactSheet> ReviewedContactSheets { get; init; } = [];
}

internal sealed record ReviewedContactSheet
{
    public string FileName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record ArchitectureCurationResult(
    ReferenceCatalog Catalog,
    IReadOnlyList<ReferenceAsset> QuarantinedAssets);

internal sealed record CostumeCurationDecision
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string CatalogLibraryId { get; init; } = string.Empty;
    public string Reviewer { get; init; } = string.Empty;
    public DateTimeOffset ReviewedAtUtc { get; init; }
    public string Policy { get; init; } = string.Empty;
    public int ExpectedCostumeAssets { get; init; }
    public string CostumeOrderSha256 { get; init; } = string.Empty;
    public IReadOnlyList<CostumeQuarantineDecision> QuarantinedAssets { get; init; } = [];
}

internal sealed record CostumeQuarantineDecision
{
    public string AssetId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string EvidenceSourceUrl { get; init; } = string.Empty;
}

internal sealed record CostumeCurationResult(
    ReferenceCatalog Catalog,
    IReadOnlyList<ReferenceAsset> QuarantinedAssets);

internal sealed record ExpansionCoverageReport
{
    public string SchemaVersion { get; init; } = "2.0.0";
    public string PlanId { get; init; } = string.Empty;
    public string CatalogLibraryId { get; init; } = string.Empty;
    public int TotalAssets { get; init; }
    public bool Passes { get; init; }
    public IReadOnlyList<string> FailedTargetIds { get; init; } = [];
    public IReadOnlyList<string> FailedCategoryIds { get; init; } = [];
    public IReadOnlyList<CategoryCoverageResult> Categories { get; init; } = [];
    public IReadOnlyList<ExpansionTargetResult> Targets { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

internal sealed record CategoryCoverageResult
{
    public string CategoryId { get; init; } = string.Empty;
    public int RequiredAssets { get; init; }
    public int ActualAssets { get; init; }
    public bool Passes { get; init; }
}

internal sealed record ExpansionTargetResult
{
    public string TargetId { get; init; } = string.Empty;
    public string Dimension { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int RequiredAssets { get; init; }
    public int ActualAssets { get; init; }
    public int? RequiredArchitectureAssets { get; init; }
    public int? ActualArchitectureAssets { get; init; }
    public int? RequiredNaturalAssets { get; init; }
    public int? ActualNaturalAssets { get; init; }
    public bool Passes { get; init; }
}

internal sealed record ExpansionTargetDescriptor(string Dimension, ExpansionTarget Target)
{
    public string TargetId => $"{Dimension}.{Target.Id}";
}

internal sealed record WikimediaCandidate(
    string Id,
    string Title,
    string Creator,
    string? CreatorUrl,
    string Source,
    Uri SourcePageUrl,
    Uri DownloadUrl,
    string License,
    string LicenseVersion,
    Uri LicenseUrl,
    int Width,
    int Height,
    string EvidenceText);

internal sealed record CatalogDiagnostic(string Code, string Path, string Message)
{
    public override string ToString() => $"{Code} {Path}: {Message}";
}

internal sealed record OpenverseCandidate(
    string Id,
    string Title,
    string Creator,
    string? CreatorUrl,
    string Source,
    Uri SourcePageUrl,
    Uri DownloadUrl,
    string License,
    string LicenseVersion,
    Uri LicenseUrl,
    int Width,
    int Height,
    string EvidenceText);

internal sealed record DownloadedImage(
    byte[] Bytes,
    string MediaType,
    string Extension,
    int Width,
    int Height);
