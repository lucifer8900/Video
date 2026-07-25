using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static partial class VisualAnchorAdoptionValidator
{
    private const string ExpectedSource = "content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png";
    private const string ExpectedTarget = "unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/identity_celadon_scout_v3.png";
    private const string ExpectedMeta = ExpectedTarget + ".meta";
    private const string ExpectedResource = "Generated/Characters/identity_celadon_scout_v3";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static IReadOnlyList<CatalogDiagnostic> Validate(string manifestPath, string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-511 adoption JSON was empty.");
        var root = Path.GetFullPath(repositoryRoot);

        ValidatePolicy(manifest, diagnostics);
        ValidateSource(manifest, root, diagnostics);
        ValidateTarget(manifest, root, diagnostics);
        ValidateHistory(root, diagnostics);
        ValidateUnityRegistration(root, diagnostics);
        return diagnostics;
    }

    private static void ValidatePolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var review = manifest["humanReview"]?.AsObject();
        var policy = manifest["generationPolicy"]?.AsObject();
        var propagation = manifest["propagation"]?.AsObject();
        if (Value(manifest, "targetPath") != ExpectedTarget)
        {
            diagnostics.Add(new("target.runtime_path", "$.targetPath", "The adopted asset must stay in Unity Resources under the versioned v3 path."));
        }
        if (Value(manifest, "targetMetaPath") != ExpectedMeta)
        {
            diagnostics.Add(new("target.meta_missing", "$.targetMetaPath", "The adoption record must point to the v3 Unity .meta file."));
        }
        if (manifest["overwriteExistingAssets"]?.GetValue<bool>() == true)
        {
            diagnostics.Add(new("target.overwrite", "$.overwriteExistingAssets", "Adoption must never overwrite an existing asset."));
        }

        if (Value(manifest, "cardId") != "CX-511" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            Value(manifest, "sourceAssetId") != "identity.celadon_scout.v3" ||
            Value(manifest, "sourcePath") != ExpectedSource ||
            Value(manifest, "targetPath") != ExpectedTarget ||
            Value(manifest, "targetMetaPath") != ExpectedMeta ||
            Value(manifest, "runtimeResourcePath") != ExpectedResource ||
            Value(manifest, "adoptionStatus") != "adopted" ||
            manifest["overwriteExistingAssets"]?.GetValue<bool>() != false ||
            manifest["shipInBuild"]?.GetValue<bool>() != true ||
            review is null ||
            review["approved"]?.GetValue<bool>() != true ||
            Value(review, "decision") != "user_explicit" ||
            policy is null ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["geminiOrVeoUsed"]?.GetValue<bool>() != false ||
            policy["sourceCandidateWasNeedsReview"]?.GetValue<bool>() != true ||
            policy["realPeopleIdentityReuseAllowed"]?.GetValue<bool>() != false ||
            propagation is null ||
            propagation["sourceCandidatePreserved"]?.GetValue<bool>() != true ||
            propagation["priorVersionsPreserved"]?.GetValue<bool>() != true ||
            Value(propagation, "catalogConstant") != "CeladonScoutIdentity" ||
            Value(propagation, "resourcesLoadPath") != ExpectedResource)
        {
            diagnostics.Add(new("policy.fail_closed", "$", "CX-511 requires explicit human adoption, shipInBuild, no overwrite, preserved history and no Gemini/Veo."));
        }
    }

    private static void ValidateSource(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var artifact = manifest["sourceArtifact"]?.AsObject();
        var sourcePath = Resolve(repositoryRoot, ExpectedSource);
        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(new("source.file_missing", "$.sourcePath", "The approved CX-510 v3 candidate is missing."));
            return;
        }

        var bytes = File.ReadAllBytes(sourcePath);
        ValidatePng(bytes, "source", diagnostics);
        ValidateArtifact(artifact, bytes, "source", diagnostics);
    }

    private static void ValidateTarget(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var sourcePath = Resolve(repositoryRoot, ExpectedSource);
        var targetPath = Resolve(repositoryRoot, Value(manifest, "targetPath"));
        var metaPath = Resolve(repositoryRoot, Value(manifest, "targetMetaPath"));
        if (!File.Exists(targetPath))
        {
            diagnostics.Add(new("target.file_missing", "$.targetPath", "The adopted v3 Unity Resources texture is missing."));
            return;
        }

        if (!File.Exists(metaPath))
        {
            diagnostics.Add(new("target.meta_missing", "$.targetMetaPath", "The adopted Unity texture must have a checked-in .meta file."));
        }
        else
        {
            var meta = File.ReadAllText(metaPath);
            if (!GuidLine().IsMatch(meta))
            {
                diagnostics.Add(new("target.meta_invalid", "$.targetMetaPath", "The Unity .meta file must contain a non-empty 32-character GUID."));
            }
        }

        var sourceBytes = File.Exists(sourcePath) ? File.ReadAllBytes(sourcePath) : [];
        var targetBytes = File.ReadAllBytes(targetPath);
        ValidatePng(targetBytes, "target", diagnostics);
        if (sourceBytes.Length == 0 || !sourceBytes.AsSpan().SequenceEqual(targetBytes))
        {
            diagnostics.Add(new("target.source_drift", "$.targetPath", "The Unity asset must be an exact byte-for-byte copy of the approved v3 candidate."));
        }
    }

    private static void ValidateHistory(string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var historyPath = Resolve(repositoryRoot, "content/story/red-mist/red-mist-visual-anchor-correction.cx510.json");
        if (!File.Exists(historyPath))
        {
            diagnostics.Add(new("source.history_missing", "$.sourceAssetId", "The CX-510 review manifest must remain available as immutable provenance."));
            return;
        }

        var history = JsonNode.Parse(File.ReadAllText(historyPath))?.AsObject();
        var candidate = history?["candidate"]?.AsObject();
        if (Value(history, "cardId") != "CX-510" ||
            Value(candidate, "assetId") != "identity.celadon_scout.v3" ||
            Value(candidate, "adoptionStatus") != "needs_review" ||
            candidate?["adopted"]?.GetValue<bool>() != false ||
            candidate?["shipInBuild"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("source.history_mutated", "$.sourceAssetId", "Adoption must not rewrite the CX-510 review-only provenance manifest."));
        }
    }

    private static void ValidateUnityRegistration(string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var catalogPath = Resolve(repositoryRoot, "unity/RedMistVerticalSlice/Assets/Scripts/Presentation/GeneratedArtCatalog.cs");
        var builderPath = Resolve(repositoryRoot, "unity/RedMistVerticalSlice/Assets/Editor/VerticalSliceBuilder.cs");
        if (!File.Exists(catalogPath) || !File.ReadAllText(catalogPath).Contains(
                "CeladonScoutIdentity = \"Generated/Characters/identity_celadon_scout_v3\"", StringComparison.Ordinal))
        {
            diagnostics.Add(new("unity.catalog_missing", "$.propagation.catalogConstant", "GeneratedArtCatalog must register the adopted v3 Resources path."));
        }
        if (!File.Exists(builderPath) || !File.ReadAllText(builderPath).Contains(
                "GeneratedArtCatalog.CeladonScoutIdentity", StringComparison.Ordinal))
        {
            diagnostics.Add(new("unity.build_registration_missing", "$.propagation", "VerticalSliceBuilder must validate the adopted v3 asset for builds."));
        }
    }

    private static void ValidateArtifact(JsonObject? artifact, byte[] bytes, string label, List<CatalogDiagnostic> diagnostics)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var width = bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(PngSignature) && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            ? ReadBigEndian(bytes, 16)
            : 0;
        var height = bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(PngSignature) && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            ? ReadBigEndian(bytes, 20)
            : 0;
        if (artifact is null ||
            Value(artifact, "mediaType") != "image/png" ||
            Number(artifact, "byteLength") != bytes.LongLength ||
            Value(artifact, "sha256") != actualHash ||
            Number(artifact, "width") != width ||
            Number(artifact, "height") != height)
        {
            diagnostics.Add(new("source.hash_mismatch", "$.sourceArtifact", $"The {label} artifact metadata does not match the file."));
        }
    }

    private static void ValidatePng(byte[] bytes, string label, List<CatalogDiagnostic> diagnostics)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            diagnostics.Add(new(label == "source" ? "source.png_signature" : "target.png_signature", "$", $"The {label} asset must be a PNG with an IHDR."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (width != 1664 || height != 936 || (long)width * 9 != (long)height * 16)
        {
            diagnostics.Add(new(label == "source" ? "source.aspect_ratio" : "target.aspect_ratio", "$", $"The {label} asset must be exact 1664x936 16:9."));
        }
    }

    private static string Resolve(string repositoryRoot, string relative) =>
        Path.GetFullPath(Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Value(JsonObject? value, string property) => value?[property]?.GetValue<string>() ?? string.Empty;
    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;
    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    [GeneratedRegex(@"(?m)^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GuidLine();
}
