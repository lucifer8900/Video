using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class MultiviewAdoptionValidator
{
    private static readonly string[] RequiredAssetIds =
    [
        "turnaround.outfit.shen_yan_qinglan_travel.v1",
        "turnaround.outfit.shen_yan_xuanheng_ceremony.v1",
        "turnaround.outfit.shen_yan_yaoyuan_combat.v1",
        "turnaround.outfit.chu_mingqi_jiyue_travel.v1",
        "turnaround.outfit.chu_mingqi_danque_ceremony.v1",
        "turnaround.outfit.chu_mingqi_chixiao_combat.v1",
        "turnaround.outfit.shi_jun_cangjin_command.v1",
        "turnaround.outfit.shi_jun_anyao_ritual.v1",
        "turnaround.outfit.celadon_scout_listening.v1",
        "turnaround.outfit.celadon_scout_night_patrol.v1",
        "turnaround.outfit.cavern_ally_stone_lamp.v1",
        "turnaround.outfit.formation_spirit_star_balance.v1",
        "turnaround.creature.ink_dragon.v1",
    ];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static IReadOnlyList<CatalogDiagnostic> Validate(
        string manifestPath,
        string sourceManifestPath,
        string candidateRoot,
        string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-518 adoption JSON was empty.");
        var source = JsonNode.Parse(File.ReadAllText(sourceManifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-517 source JSON was empty.");

        ValidateSourceManifest(manifest, sourceManifestPath, diagnostics);
        ValidateReview(manifest, diagnostics);
        ValidatePolicy(manifest, diagnostics);

        var assets = Objects(manifest, "assets");
        var sourceCandidates = Objects(source, "candidates");
        ValidateCoverage(manifest, source, assets, sourceCandidates, diagnostics);
        for (var index = 0; index < Math.Min(assets.Length, sourceCandidates.Length); index++)
        {
            ValidateAsset(assets[index], sourceCandidates[index], index, candidateRoot, diagnostics);
        }

        ValidateMarkdown(manifest, assets, repositoryRoot, diagnostics);
        return diagnostics;
    }

    private static void ValidateSourceManifest(
        JsonObject manifest,
        string sourceManifestPath,
        List<CatalogDiagnostic> diagnostics)
    {
        var source = manifest["sourceManifest"]?.AsObject();
        var actualHash = Sha256(File.ReadAllBytes(sourceManifestPath));
        if (source is null ||
            Value(source, "cardId") != "CX-517" ||
            Value(source, "path") != "content/story/red-mist/red-mist-multiview-candidates.cx517.json" ||
            source["preserved"]?.GetValue<bool>() != true ||
            !string.Equals(Value(source, "sha256"), actualHash, StringComparison.Ordinal))
        {
            diagnostics.Add(new("source.manifest_hash", "$.sourceManifest", "CX-518 must pin and preserve the exact CX-517 manifest SHA-256."));
        }
    }

    private static void ValidateReview(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var review = manifest["humanReview"]?.AsObject();
        if (Value(manifest, "cardId") != "CX-518" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            review is null ||
            review["approved"]?.GetValue<bool>() != true ||
            Value(review, "decision") != "user_explicit_all" ||
            Value(review, "reviewedAt") != "2026-08-03" ||
            Value(review, "scope") != "all_13_cx517_candidates")
        {
            diagnostics.Add(new("review.explicit", "$.humanReview", "All thirteen candidates require the user's explicit dated approval."));
        }
    }

    private static void ValidatePolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["adoptionPolicy"]?.AsObject();
        if (policy is null || policy["adoptedAsReference"]?.GetValue<bool>() != true)
            diagnostics.Add(new("adoption.reference", "$.adoptionPolicy.adoptedAsReference", "All approved boards must become reference anchors."));
        if (policy is null || policy["expressionGenerationAllowed"]?.GetValue<bool>() != true)
            diagnostics.Add(new("adoption.expressions", "$.adoptionPolicy.expressionGenerationAllowed", "Approval must unlock the separate expression generation card."));
        if (policy is null || policy["runtimeUse"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.runtime", "$.adoptionPolicy.runtimeUse", "Turnaround boards are not runtime game assets."));
        if (policy is null || policy["shipInBuild"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.shipping", "$.adoptionPolicy.shipInBuild", "Turnaround boards must stay out of shipped builds."));
        if (policy is null || policy["overwriteExistingAssets"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.overwrite", "$.adoptionPolicy.overwriteExistingAssets", "Adoption must not overwrite prior assets."));
        if (policy is null ||
            policy["newImageGeneration"]?.GetValue<bool>() != false ||
            policy["videoOrAudioGeneration"]?.GetValue<bool>() != false ||
            policy["imagegenUsed"]?.GetValue<bool>() != false ||
            policy["flow2ApiUsed"]?.GetValue<bool>() != false ||
            policy["geminiUsed"]?.GetValue<bool>() != false ||
            policy["veoUsed"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("policy.no_generation", "$.adoptionPolicy", "CX-518 records approval only and must not generate image, video or audio assets."));
        }
        if (policy is null || Value(policy, "cx514Status") != "paused")
            diagnostics.Add(new("policy.cx514_paused", "$.adoptionPolicy.cx514Status", "CX-514 must remain paused."));
    }

    private static void ValidateCoverage(
        JsonObject manifest,
        JsonObject source,
        IReadOnlyList<JsonObject> assets,
        IReadOnlyList<JsonObject> sourceCandidates,
        List<CatalogDiagnostic> diagnostics)
    {
        var adoptedIds = Strings(manifest, "adoptedAssetIds");
        var assetIds = assets.Select(asset => Value(asset, "assetId")).ToArray();
        var sourceIds = sourceCandidates.Select(asset => Value(asset, "assetId")).ToArray();
        var sourceRequired = Strings(source, "requiredCandidateIds");
        if (!adoptedIds.SequenceEqual(RequiredAssetIds, StringComparer.Ordinal) ||
            !assetIds.SequenceEqual(RequiredAssetIds, StringComparer.Ordinal) ||
            !sourceIds.SequenceEqual(RequiredAssetIds, StringComparer.Ordinal) ||
            !sourceRequired.SequenceEqual(RequiredAssetIds, StringComparer.Ordinal))
        {
            diagnostics.Add(new("coverage.assets", "$.assets", "CX-518 must adopt the complete ordered thirteen-item CX-517 set."));
        }
    }

    private static void ValidateAsset(
        JsonObject asset,
        JsonObject sourceCandidate,
        int index,
        string candidateRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.assets[{index}]";
        if (Value(asset, "assetId") != Value(sourceCandidate, "assetId") ||
            Value(asset, "sourcePath") != Value(sourceCandidate, "outputPath"))
        {
            diagnostics.Add(new("source.path_mapping", path, "Adopted assets must retain the exact CX-517 asset ID and source path."));
        }

        if (Value(asset, "sourceAdoptionStatus") != "needs_review" ||
            Value(sourceCandidate, "adoptionStatus") != "needs_review" ||
            sourceCandidate["adopted"]?.GetValue<bool>() != false ||
            sourceCandidate["shipInBuild"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("source.provenance", path, "CX-517 review-only provenance must remain unchanged."));
        }

        if (asset["adoptedAsReference"]?.GetValue<bool>() != true)
            diagnostics.Add(new("adoption.reference", path, "Every item must be adopted as a generation reference."));
        if (asset["expressionGenerationAllowed"]?.GetValue<bool>() != true)
            diagnostics.Add(new("adoption.expressions", path, "Every approved humanoid reference must be available to the expression card."));
        if (asset["runtimeUse"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.runtime", path, "No turnaround board may become a runtime asset."));
        if (asset["shipInBuild"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.shipping", path, "No turnaround board may ship in the build."));

        ValidateArtifact(asset, sourceCandidate, candidateRoot, path, diagnostics);
    }

    private static void ValidateArtifact(
        JsonObject asset,
        JsonObject sourceCandidate,
        string candidateRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var root = Path.GetFullPath(candidateRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, Path.GetFileName(Value(asset, "sourcePath"))));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            diagnostics.Add(new("source.file_metadata", path + ".sourcePath", "Approved source file is missing or outside the candidate root."));
            return;
        }

        var bytes = File.ReadAllBytes(resolved);
        var artifact = asset["artifact"]?.AsObject();
        var sourceArtifact = sourceCandidate["artifact"]?.AsObject();
        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            artifact is null ||
            Value(artifact, "mediaType") != "image/png")
        {
            diagnostics.Add(new("source.png_signature", path + ".artifact", "Approved source must remain a valid PNG with IHDR and PNG media type."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (Number(artifact, "byteLength") != bytes.LongLength ||
            Number(artifact, "width") != width ||
            Number(artifact, "height") != height ||
            width != 1536 || height != 1024 ||
            sourceArtifact is null ||
            Number(sourceArtifact, "byteLength") != bytes.LongLength ||
            Number(sourceArtifact, "width") != width ||
            Number(sourceArtifact, "height") != height)
        {
            diagnostics.Add(new("source.file_metadata", path + ".artifact", "Source and adoption length and 1536x1024 dimensions must match the PNG."));
        }

        var actualHash = Sha256(bytes);
        if (!string.Equals(Value(artifact, "sha256"), actualHash, StringComparison.Ordinal) ||
            !string.Equals(Value(sourceArtifact, "sha256"), actualHash, StringComparison.Ordinal))
        {
            diagnostics.Add(new("source.file_hash", path + ".artifact.sha256", "Source and adoption SHA-256 must match the PNG."));
        }
    }

    private static void ValidateMarkdown(
        JsonObject manifest,
        IReadOnlyList<JsonObject> assets,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var contract = manifest["markdownContract"]?.AsObject();
        var relative = contract is null ? string.Empty : Value(contract, "relativePath");
        var root = Path.GetFullPath(repositoryRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            diagnostics.Add(new("markdown.contract", "$.markdownContract", "CX-518 readable adoption record is missing."));
            return;
        }

        var markdown = File.ReadAllText(resolved);
        if (assets.Any(asset =>
                !markdown.Contains(Value(asset, "assetId"), StringComparison.Ordinal) ||
                !markdown.Contains(Path.GetFileName(Value(asset, "sourcePath")), StringComparison.Ordinal)))
        {
            diagnostics.Add(new("markdown.contract", "$.markdownContract", "Markdown must list every adopted asset ID and source filename."));
        }
    }

    private static JsonObject[] Objects(JsonObject value, string property) =>
        value[property]?.AsArray().Where(node => node is not null).Select(node => node!.AsObject()).ToArray() ?? [];

    private static string[] Strings(JsonObject value, string property) =>
        value[property]?.AsArray().Select(node => node?.GetValue<string>() ?? string.Empty).ToArray() ?? [];

    private static string Value(JsonObject? value, string property) => value?[property]?.GetValue<string>() ?? string.Empty;
    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
