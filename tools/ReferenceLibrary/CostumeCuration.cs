using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class CostumeCuration
{
    public static CostumeCurationResult Apply(
        ReferenceCatalog catalog,
        CostumeCurationDecision decision)
    {
        if (decision.SchemaVersion != "1.0.0")
        {
            throw new InvalidDataException("Costume curation decision schemaVersion must be 1.0.0.");
        }

        if (decision.CatalogLibraryId != catalog.LibraryId)
        {
            throw new InvalidDataException("Costume curation decision targets a different catalog library.");
        }

        var costumes = catalog.Assets
            .Where(asset => asset.Category == "costumes_textiles")
            .ToArray();
        if (costumes.Length != decision.ExpectedCostumeAssets)
        {
            throw new InvalidDataException(
                $"Costume snapshot count changed: expected {decision.ExpectedCostumeAssets}, found {costumes.Length}.");
        }

        if (!ComputeOrderSha256(costumes).Equals(
                decision.CostumeOrderSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Costume catalog order fingerprint changed after period review.");
        }

        var reviewedIds = decision.QuarantinedAssets
            .Select(item => item.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        if (reviewedIds.Count != decision.QuarantinedAssets.Count ||
            decision.QuarantinedAssets.Any(item =>
                string.IsNullOrWhiteSpace(item.Reason) ||
                !Uri.TryCreate(item.EvidenceSourceUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException(
                "Costume quarantine decisions must have unique IDs, reasons, and HTTPS evidence URLs.");
        }

        var costumeIds = costumes.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        if (reviewedIds.Any(id => !costumeIds.Contains(id)))
        {
            throw new InvalidDataException("Costume quarantine decision contains an unknown costume asset ID.");
        }

        var quarantined = costumes.Where(asset => reviewedIds.Contains(asset.Id)).ToArray();
        var curated = catalog.Assets.Where(asset => !reviewedIds.Contains(asset.Id)).ToArray();
        return new CostumeCurationResult(catalog with { Assets = curated }, quarantined);
    }

    internal static string ComputeOrderSha256(IReadOnlyList<ReferenceAsset> assets)
    {
        var input = string.Join('\n', assets.Select(asset => asset.Id));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
