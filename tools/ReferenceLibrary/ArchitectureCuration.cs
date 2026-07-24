using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ArchitectureCuration
{
    private const string RawPrefix = "content/reference-library/raw/";

    public static ArchitectureCurationResult Apply(
        ReferenceCatalog catalog,
        ArchitectureCurationDecision decision)
    {
        if (decision.SchemaVersion != "1.0.0")
        {
            throw new InvalidDataException("Architecture curation decision schemaVersion must be 1.0.0.");
        }

        if (decision.CatalogLibraryId != catalog.LibraryId)
        {
            throw new InvalidDataException("Architecture curation decision targets a different catalog library.");
        }

        if (!decision.QuarantineUnlisted)
        {
            throw new InvalidDataException("Architecture curation must quarantine every unlisted asset.");
        }

        var architecture = catalog.Assets
            .Where(asset => asset.Category == "architecture")
            .ToArray();
        if (architecture.Length != decision.ExpectedArchitectureAssets)
        {
            throw new InvalidDataException(
                $"Architecture snapshot count changed: expected {decision.ExpectedArchitectureAssets}, found {architecture.Length}.");
        }

        var fingerprint = ComputeOrderSha256(architecture);
        if (!fingerprint.Equals(decision.ArchitectureOrderSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Architecture catalog order fingerprint changed after visual review.");
        }

        var confirmedIndices = decision.ConfirmedAbsentIndices.ToHashSet();
        if (confirmedIndices.Count != decision.ConfirmedAbsentIndices.Count ||
            confirmedIndices.Any(index => index < 1 || index > architecture.Length))
        {
            throw new InvalidDataException("Confirmed architecture indices must be unique and within the reviewed snapshot.");
        }

        var confirmedIds = confirmedIndices
            .Select(index => architecture[index - 1].Id)
            .ToHashSet(StringComparer.Ordinal);
        var quarantined = architecture
            .Where(asset => !confirmedIds.Contains(asset.Id))
            .ToArray();
        var curatedAssets = catalog.Assets
            .Where(asset => asset.Category != "architecture" || confirmedIds.Contains(asset.Id))
            .Select(asset => asset with
            {
                Taxonomy = asset.Taxonomy with
                {
                    PeoplePresence = asset.Category == "architecture"
                        ? "confirmed_absent"
                        : "not_applicable",
                },
            })
            .ToArray();

        return new ArchitectureCurationResult(
            catalog with { Assets = curatedAssets },
            quarantined);
    }

    public static void QuarantineFiles(
        IReadOnlyList<ReferenceAsset> assets,
        string downloadsRoot,
        string quarantineRoot)
    {
        var sourceRoot = EnsureDirectoryRoot(downloadsRoot);
        var targetRoot = EnsureDirectoryRoot(quarantineRoot);

        foreach (var asset in assets)
        {
            if (!asset.LocalRelativePath.StartsWith(RawPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Asset {asset.Id} is outside the reference raw boundary.");
            }

            var relative = asset.LocalRelativePath[RawPrefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var source = EnsureChildPath(sourceRoot, relative);
            var target = EnsureChildPath(targetRoot, relative);

            if (!File.Exists(source))
            {
                if (File.Exists(target) && FileSha256(target).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw new FileNotFoundException($"Reviewed architecture file is missing: {asset.LocalRelativePath}", source);
            }

            if (!FileSha256(source).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Reviewed architecture file hash changed: {asset.Id}.");
            }

            if (File.Exists(target))
            {
                throw new IOException($"Quarantine target already exists while source is still present: {target}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target);
        }
    }

    internal static string ComputeOrderSha256(IReadOnlyList<ReferenceAsset> assets)
    {
        var input = string.Join('\n', assets.Select(asset => asset.Id));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string EnsureDirectoryRoot(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static string EnsureChildPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Architecture curation path escapes its configured root.");
        }

        return path;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
