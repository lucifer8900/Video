using System.Security.Cryptography;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class CatalogPolicyValidator
{
    private const string RawPrefix = "content/reference-library/raw/";
    private static readonly HashSet<string> AllowedLicenses = new(StringComparer.Ordinal)
    {
        "cc0", "pdm", "by",
    };

    private static readonly HashSet<string> DiscoveryOnlyHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "google.com",
        "www.google.com",
        "images.google.com",
        "bing.com",
        "www.bing.com",
        "image.baidu.com",
        "baidu.com",
        "www.baidu.com",
        "pinterest.com",
        "www.pinterest.com",
    };

    public static IReadOnlyList<CatalogDiagnostic> Validate(
        ReferenceCatalog catalog,
        string downloadsRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var root = Path.GetFullPath(downloadsRoot);

        if (catalog.SchemaVersion != "2.0.0")
        {
            diagnostics.Add(new("catalog.version", "/schemaVersion", "Expected schemaVersion 2.0.0."));
        }

        if (catalog.CoveragePlanId != "cx507-china-visual-atlas-v1")
        {
            diagnostics.Add(new("catalog.coverage_plan", "/coveragePlanId", "Catalog must bind the approved CX-507 plan."));
        }

        if (!catalog.ReferenceOnly || catalog.ShipInBuild)
        {
            diagnostics.Add(new("catalog.shipping_boundary", "", "The reference library must remain reference-only and outside builds."));
        }

        if (!catalog.AllowedLicenses.SequenceEqual(AllowedLicenses.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            !catalog.AllowedLicenses.SequenceEqual(["cc0", "pdm", "by"], StringComparer.Ordinal))
        {
            diagnostics.Add(new("license.catalog_policy", "/allowedLicenses", "Catalog license policy must be cc0, pdm, by."));
        }

        if (catalog.Categories.Count < 12 || catalog.Categories.Distinct(StringComparer.Ordinal).Count() != catalog.Categories.Count)
        {
            diagnostics.Add(new("category.catalog", "/categories", "At least 12 unique categories are required."));
        }

        if (catalog.Assets.Count < catalog.Categories.Count * catalog.MinimumAssetsPerCategory)
        {
            diagnostics.Add(new("category.total", "/assets", "Catalog does not meet the minimum total asset count."));
        }


        if (catalog.Assets.Count < 800)
        {
            diagnostics.Add(new("coverage.minimum_total", "/assets", $"CX-507 requires at least 800 assets; found {catalog.Assets.Count}."));
        }

        foreach (var category in catalog.Categories)
        {
            var count = catalog.Assets.Count(asset => asset.Category == category);
            if (count < catalog.MinimumAssetsPerCategory)
            {
                diagnostics.Add(new("category.minimum", "/assets", $"Category {category} has {count} assets."));
            }
        }

        foreach (var duplicate in catalog.Assets.GroupBy(asset => asset.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new("asset.duplicate_id", "/assets", $"Duplicate id {duplicate.Key}."));
        }

        AddDuplicateDiagnostics(catalog.Assets, asset => asset.SourcePageUrl, "source_page", diagnostics);
        AddDuplicateDiagnostics(catalog.Assets, asset => asset.DownloadUrl, "download_url", diagnostics);
        AddDuplicateDiagnostics(catalog.Assets, asset => asset.Sha256, "sha256", diagnostics);

        for (var index = 0; index < catalog.Assets.Count; index++)
        {
            ValidateAsset(catalog.Assets[index], index, root, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateAsset(
        ReferenceAsset asset,
        int index,
        string downloadsRoot,
        ICollection<CatalogDiagnostic> diagnostics)
    {
        var path = $"/assets/{index}";
        if (!AllowedLicenses.Contains(asset.License))
        {
            diagnostics.Add(new("license.not_allowed", path + "/license", asset.License));
        }

        if (asset.ShareAlikeRequired)
        {
            diagnostics.Add(new("license.share_alike", path + "/shareAlikeRequired", "Share-alike sources are outside CX-506 policy."));
        }

        if (!asset.CommercialUseAllowed || !asset.ModificationsAllowed)
        {
            diagnostics.Add(new("license.rights", path, "Commercial use and modification must both be allowed."));
        }

        if (!asset.ReferenceOnly || asset.ShipInBuild)
        {
            diagnostics.Add(new("asset.shipping_boundary", path, "Reference photos must not ship in the game build."));
        }

        if (asset.IdentityReuse)
        {
            diagnostics.Add(new(
                asset.Category == "people" ? "people.identity_reuse" : "asset.identity_reuse",
                path + "/identityReuse",
                "Recognizable identity reuse is forbidden."));
        }

        if (asset.CurationStatus != "needs_review" || asset.CoverageTargetIds.Count == 0)
        {
            diagnostics.Add(new("asset.review_boundary", path, "Every CX-507 asset remains needs_review and must have coverage provenance."));
        }

        if (asset.Taxonomy is null || string.IsNullOrWhiteSpace(asset.Taxonomy.Authenticity))
        {
            diagnostics.Add(new("asset.taxonomy", path + "/taxonomy", "Strict taxonomy and authenticity state are required."));
        }

        if (!TryHttpsUri(asset.SourcePageUrl, out var sourcePage))
        {
            diagnostics.Add(new("source.invalid", path + "/sourcePageUrl", "A direct HTTPS source page is required."));
        }
        else if (DiscoveryOnlyHosts.Contains(sourcePage.Host))
        {
            diagnostics.Add(new("source.discovery_only", path + "/sourcePageUrl", sourcePage.Host));
        }

        if (!TryHttpsUri(asset.DownloadUrl, out _))
        {
            diagnostics.Add(new("source.download_url", path + "/downloadUrl", "An HTTPS download URL is required."));
        }

        if (!TryHttpsUri(asset.LicenseUrl, out _))
        {
            diagnostics.Add(new("license.url", path + "/licenseUrl", "An HTTPS license URL is required."));
        }

        if (string.IsNullOrWhiteSpace(asset.Creator))
        {
            diagnostics.Add(new("source.creator", path + "/creator", "Creator metadata is required."));
        }

        if (!TryResolveRawPath(asset.LocalRelativePath, asset.Category, downloadsRoot, out var localPath))
        {
            diagnostics.Add(new("path.outside_raw", path + "/localRelativePath", asset.LocalRelativePath));
            return;
        }

        if (!File.Exists(localPath))
        {
            diagnostics.Add(new("file.missing", path + "/localRelativePath", localPath));
            return;
        }

        var fileInfo = new FileInfo(localPath);
        if (fileInfo.Length != asset.ByteLength)
        {
            diagnostics.Add(new("file.length", path + "/byteLength", $"Expected {asset.ByteLength}, got {fileInfo.Length}."));
        }

        using var stream = File.OpenRead(localPath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, asset.Sha256, StringComparison.Ordinal))
        {
            diagnostics.Add(new("file.hash", path + "/sha256", $"Expected {asset.Sha256}, got {hash}."));
        }

        stream.Position = 0;
        if (!ReferenceImageInspector.MatchesMediaType(stream, asset.MediaType))
        {
            diagnostics.Add(new("file.signature", path + "/mediaType", "File signature does not match mediaType."));
        }
    }

    private static bool TryResolveRawPath(
        string localRelativePath,
        string category,
        string downloadsRoot,
        out string fullPath)
    {
        fullPath = string.Empty;
        var normalized = localRelativePath.Replace('\\', '/');
        var expectedPrefix = RawPrefix + category + "/";
        if (!normalized.StartsWith(expectedPrefix, StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal))
        {
            return false;
        }

        var relativeToRaw = normalized[RawPrefix.Length..]
            .Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(downloadsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        fullPath = Path.GetFullPath(Path.Combine(root, relativeToRaw));
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryHttpsUri(string value, out Uri uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        uri = parsed ?? new Uri("https://invalid.invalid/");
        return valid;
    }

    private static void AddDuplicateDiagnostics(
        IReadOnlyList<ReferenceAsset> assets,
        Func<ReferenceAsset, string> selector,
        string field,
        ICollection<CatalogDiagnostic> diagnostics)
    {
        foreach (var duplicate in assets.GroupBy(selector, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new($"asset.duplicate_{field}", "/assets", duplicate.Key));
        }
    }
}
