using System.Text.Json;

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

internal sealed record ReferenceCatalog
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
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
}

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

internal sealed record DownloadedImage(byte[] Bytes, string MediaType, string Extension);
