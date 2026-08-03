using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static partial class MultiviewCandidateValidator
{
    private static readonly string[] RequiredCandidateIds =
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

    private static readonly Dictionary<string, string> RequiredOutfits = new(StringComparer.Ordinal)
    {
        [RequiredCandidateIds[0]] = "outfit.shen_yan_qinglan_travel.v1",
        [RequiredCandidateIds[1]] = "outfit.shen_yan_xuanheng_ceremony.v1",
        [RequiredCandidateIds[2]] = "outfit.shen_yan_yaoyuan_combat.v1",
        [RequiredCandidateIds[3]] = "outfit.chu_mingqi_jiyue_travel.v1",
        [RequiredCandidateIds[4]] = "outfit.chu_mingqi_danque_ceremony.v1",
        [RequiredCandidateIds[5]] = "outfit.chu_mingqi_chixiao_combat.v1",
        [RequiredCandidateIds[6]] = "outfit.shi_jun_cangjin_command.v1",
        [RequiredCandidateIds[7]] = "outfit.shi_jun_anyao_ritual.v1",
        [RequiredCandidateIds[8]] = "outfit.celadon_scout_listening.v1",
        [RequiredCandidateIds[9]] = "outfit.celadon_scout_night_patrol.v1",
        [RequiredCandidateIds[10]] = "outfit.cavern_ally_stone_lamp.v1",
        [RequiredCandidateIds[11]] = "outfit.formation_spirit_star_balance.v1",
    };

    private static readonly Dictionary<string, AnchorContract> RequiredAnchors = new(StringComparer.Ordinal)
    {
        ["identity.shen_yan.v1"] = new("content/visual-candidates/cx508/identity-anchors/identity_shen_yan_v1.png"),
        ["identity.chu_mingqi.v2"] = new("content/visual-candidates/cx509/identity-anchors/identity_chu_mingqi_v2.png"),
        ["identity.shi_jun.v3"] = new("content/visual-candidates/cx509/identity-anchors/identity_shi_jun_v3.png"),
        ["identity.celadon_scout.v3"] = new("content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png"),
        ["identity.cavern_ally.v1"] = new("content/visual-candidates/cx508/identity-anchors/identity_cavern_ally_v1.png"),
        ["identity.formation_spirit.v2"] = new("content/visual-candidates/cx509/identity-anchors/identity_formation_spirit_v2.png"),
        ["creature.ink_dragon.v3"] = new("content/visual-candidates/cx509/identity-anchors/creature_ink_dragon_v3.png"),
    };

    private static readonly Dictionary<string, string> CandidateAnchors = new(StringComparer.Ordinal)
    {
        [RequiredCandidateIds[0]] = "identity.shen_yan.v1",
        [RequiredCandidateIds[1]] = "identity.shen_yan.v1",
        [RequiredCandidateIds[2]] = "identity.shen_yan.v1",
        [RequiredCandidateIds[3]] = "identity.chu_mingqi.v2",
        [RequiredCandidateIds[4]] = "identity.chu_mingqi.v2",
        [RequiredCandidateIds[5]] = "identity.chu_mingqi.v2",
        [RequiredCandidateIds[6]] = "identity.shi_jun.v3",
        [RequiredCandidateIds[7]] = "identity.shi_jun.v3",
        [RequiredCandidateIds[8]] = "identity.celadon_scout.v3",
        [RequiredCandidateIds[9]] = "identity.celadon_scout.v3",
        [RequiredCandidateIds[10]] = "identity.cavern_ally.v1",
        [RequiredCandidateIds[11]] = "identity.formation_spirit.v2",
        [RequiredCandidateIds[12]] = "creature.ink_dragon.v3",
    };

    private static readonly string[] HumanViews = ["front", "left_profile", "back"];
    private static readonly string[] CreatureViews = ["front_three_quarter", "left_profile", "rear_three_quarter", "top"];
    private static readonly HashSet<string> HumanLocks =
    [
        "same_face", "same_hair", "same_anatomy", "same_outfit", "same_footwear", "same_prop",
    ];
    private static readonly HashSet<string> CommonAvoid =
    [
        "no_text", "no_watermark", "no_extra_people", "no_actor_likeness",
    ];
    private static readonly HashSet<string> CreatureLocks =
    [
        "same_identity", "same_anatomy", "same_horns", "same_whiskers", "same_mane",
        "same_scales", "exactly_four_limbs", "exactly_four_claws_per_foot", "no_wings",
    ];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static IReadOnlyList<CatalogDiagnostic> Validate(
        string manifestPath,
        string artDirectionPath,
        string candidateRoot,
        string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-517 manifest JSON was empty.");
        var artDirection = JsonNode.Parse(File.ReadAllText(artDirectionPath))?.AsObject()
            ?? throw new InvalidDataException("CX-516 art direction JSON was empty.");

        ValidatePolicy(manifest, diagnostics);
        ValidateArtDirectionSource(manifest, artDirectionPath, diagnostics);
        var anchors = ValidateAnchors(manifest, repositoryRoot, diagnostics);
        var candidates = manifest["candidates"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        ValidateCoverage(manifest, candidates, diagnostics);
        ValidateOutfitCoverage(candidates, artDirection, diagnostics);

        foreach (var (candidate, index) in candidates.Select((candidate, index) => (candidate, index)))
        {
            ValidateCandidate(candidate, index, anchors, candidateRoot, diagnostics);
        }

        ValidateMarkdown(manifest, candidates, repositoryRoot, diagnostics);
        return diagnostics;
    }

    private static void ValidatePolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["generationPolicy"]?.AsObject();
        if (manifest["cardId"]?.GetValue<string>() != "CX-517" ||
            policy is null ||
            Value(policy, "imageProvider") != "codex_builtin_imagegen" ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["videoOrAudioUsed"]?.GetValue<bool>() != false ||
            policy["flow2ApiUsed"]?.GetValue<bool>() != false ||
            policy["geminiUsed"]?.GetValue<bool>() != false ||
            policy["veoUsed"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("policy.imagegen_only", "$.generationPolicy", "CX-517 must use only built-in ImageGen for still candidates."));
        }

        if (policy is null || Value(policy, "cx514Status") != "paused")
        {
            diagnostics.Add(new("policy.cx514_paused", "$.generationPolicy.cx514Status", "CX-514 must remain paused."));
        }

        if (policy is null || policy["expressionsDeferredUntilMultiviewApproval"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("policy.expressions_deferred", "$.generationPolicy.expressionsDeferredUntilMultiviewApproval", "Expression assets must wait for multiview approval."));
        }
    }

    private static void ValidateArtDirectionSource(JsonObject manifest, string artDirectionPath, List<CatalogDiagnostic> diagnostics)
    {
        var source = manifest["artDirectionSource"]?.AsObject();
        var actual = Sha256(File.ReadAllBytes(artDirectionPath));
        if (source is null ||
            Value(source, "path") != "content/story/red-mist/red-mist-art-direction.cx516.json" ||
            !string.Equals(Value(source, "sha256"), actual, StringComparison.Ordinal))
        {
            diagnostics.Add(new("source.art_direction_hash", "$.artDirectionSource", "CX-516 path and SHA-256 must match the checked-in art direction."));
        }
    }

    private static Dictionary<string, JsonObject> ValidateAnchors(
        JsonObject manifest,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var anchors = manifest["anchorSources"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToDictionary(anchor => Value(anchor, "assetId"), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        if (!anchors.Keys.SequenceEqual(RequiredAnchors.Keys, StringComparer.Ordinal))
        {
            diagnostics.Add(new("source.anchor_mapping", "$.anchorSources", "The ordered seven approved anchors are required."));
        }

        foreach (var (assetId, contract) in RequiredAnchors)
        {
            if (!anchors.TryGetValue(assetId, out var anchor) || Value(anchor, "path") != contract.Path)
            {
                diagnostics.Add(new("source.anchor_mapping", "$.anchorSources", $"Anchor mapping for '{assetId}' is incorrect."));
                continue;
            }

            var resolved = ResolveRepositoryPath(repositoryRoot, contract.Path);
            if (resolved is null || !File.Exists(resolved) ||
                !string.Equals(Value(anchor, "sha256"), Sha256(File.ReadAllBytes(resolved)), StringComparison.Ordinal))
            {
                diagnostics.Add(new("source.anchor_hash", "$.anchorSources", $"Anchor SHA-256 for '{assetId}' does not match its file."));
            }
        }

        return anchors;
    }

    private static void ValidateCoverage(
        JsonObject manifest,
        IReadOnlyList<JsonObject> candidates,
        List<CatalogDiagnostic> diagnostics)
    {
        var declared = manifest["requiredCandidateIds"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray() ?? [];
        var actual = candidates.Select(candidate => Value(candidate, "assetId")).ToArray();
        if (!declared.SequenceEqual(RequiredCandidateIds, StringComparer.Ordinal) ||
            !actual.SequenceEqual(RequiredCandidateIds, StringComparer.Ordinal))
        {
            diagnostics.Add(new("coverage.candidates", "$.candidates", "CX-517 requires the ordered twelve outfit boards followed by the ink dragon board."));
        }
    }

    private static void ValidateOutfitCoverage(
        IReadOnlyList<JsonObject> candidates,
        JsonObject artDirection,
        List<CatalogDiagnostic> diagnostics)
    {
        var approvedOutfits = artDirection["costumePlan"]?.AsArray()
            .Where(node => node is not null)
            .SelectMany(entry => entry!["outfits"]?.AsArray() ?? [])
            .Where(node => node is not null)
            .Select(node => Value(node!.AsObject(), "outfitId"))
            .ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var candidate in candidates.Where(candidate => Value(candidate, "assetKind") == "outfit_turnaround"))
        {
            var assetId = Value(candidate, "assetId");
            var outfitId = Value(candidate, "outfitId");
            if (!RequiredOutfits.TryGetValue(assetId, out var expected) ||
                outfitId != expected ||
                !approvedOutfits.Contains(outfitId))
            {
                diagnostics.Add(new("coverage.outfits", "$.candidates", $"Candidate '{assetId}' is not mapped to its approved CX-516 outfit."));
            }
        }
    }

    private static void ValidateCandidate(
        JsonObject candidate,
        int index,
        IReadOnlyDictionary<string, JsonObject> anchors,
        string candidateRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.candidates[{index}]";
        var assetId = Value(candidate, "assetId");
        var kind = Value(candidate, "assetKind");
        var outputPath = Value(candidate, "outputPath");

        if (Value(candidate, "generationMode") != "codex_builtin_imagegen")
        {
            diagnostics.Add(new("policy.imagegen_only", path + ".generationMode", "Candidates must be generated with built-in ImageGen."));
        }

        if (Value(candidate, "adoptionStatus") != "needs_review" ||
            candidate["adopted"]?.GetValue<bool>() != false ||
            candidate["shipInBuild"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("candidate.review_only", path, "Candidates must remain unadopted and excluded from builds until human review."));
        }

        var normalized = outputPath.Replace('\\', '/');
        if (!normalized.StartsWith("content/visual-candidates/cx517/turnarounds/", StringComparison.Ordinal) ||
            normalized.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("unity/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new("candidate.runtime_path", path + ".outputPath", "CX-517 candidates must stay outside Unity runtime directories."));
        }

        if (!VersionedAssetId().IsMatch(assetId) || !VersionedPngName().IsMatch(Path.GetFileName(outputPath)))
        {
            diagnostics.Add(new("candidate.versioned_name", path, "Candidate IDs and PNG names must be explicitly versioned."));
        }

        ValidateSourceAnchor(candidate, assetId, anchors, path, diagnostics);
        ValidateViewsAndContinuity(candidate, kind, path, diagnostics);
        ValidateArtifact(candidate, outputPath, candidateRoot, path, diagnostics);
    }

    private static void ValidateSourceAnchor(
        JsonObject candidate,
        string assetId,
        IReadOnlyDictionary<string, JsonObject> anchors,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var source = candidate["sourceAnchor"]?.AsObject();
        if (source is null ||
            !CandidateAnchors.TryGetValue(assetId, out var expectedAnchor) ||
            Value(source, "assetId") != expectedAnchor ||
            !anchors.TryGetValue(expectedAnchor, out var declaredAnchor) ||
            Value(source, "path") != Value(declaredAnchor, "path") ||
            Value(source, "sha256") != Value(declaredAnchor, "sha256"))
        {
            diagnostics.Add(new("source.anchor_mapping", path + ".sourceAnchor", "Candidate source anchor must match the approved identity or creature anchor."));
        }
    }

    private static void ValidateViewsAndContinuity(
        JsonObject candidate,
        string kind,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var views = Strings(candidate, "views");
        var expectedViews = kind == "creature_turnaround" ? CreatureViews : HumanViews;
        if (!views.SequenceEqual(expectedViews, StringComparer.Ordinal))
        {
            diagnostics.Add(new("candidate.views", path + ".views", "The complete ordered multiview set is required."));
        }

        var locks = Strings(candidate, "continuityLocks").ToHashSet(StringComparer.Ordinal);
        var avoid = Strings(candidate, "avoidItems").ToHashSet(StringComparer.Ordinal);
        var prompt = Value(candidate, "prompt");
        if (prompt.Length < 180 || !CommonAvoid.IsSubsetOf(avoid) ||
            (kind == "outfit_turnaround" && !HumanLocks.IsSubsetOf(locks)))
        {
            diagnostics.Add(new("candidate.continuity_contract", path, "Prompt, avoid list and locks must preserve the same person, hair, anatomy, outfit, footwear, prop, clean frame and fictional identity."));
        }

        if (kind == "creature_turnaround" &&
            (!CreatureLocks.IsSubsetOf(locks) ||
             !avoid.Contains("no_lizard_anatomy") ||
             !avoid.Contains("no_western_dragon") ||
             !avoid.Contains("no_extra_limbs")))
        {
            diagnostics.Add(new("creature.anatomy", path, "Ink dragon must retain Chinese jiao anatomy, two horns, whiskers, mane, four four-clawed limbs and no wings."));
        }
    }

    private static void ValidateArtifact(
        JsonObject candidate,
        string outputPath,
        string candidateRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var root = Path.GetFullPath(candidateRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, Path.GetFileName(outputPath)));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            diagnostics.Add(new("candidate.file_metadata", path + ".outputPath", "Candidate file is missing or outside the declared root."));
            return;
        }

        var bytes = File.ReadAllBytes(resolved);
        var artifact = candidate["artifact"]?.AsObject();
        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            artifact is null || Value(artifact, "mediaType") != "image/png")
        {
            diagnostics.Add(new("candidate.png_signature", path + ".artifact", "Candidate must be a PNG with a valid signature, IHDR and media type."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (Number(artifact, "byteLength") != bytes.LongLength ||
            Number(artifact, "width") != width ||
            Number(artifact, "height") != height ||
            width < 1400 || height < 900 || (long)width * 2 != (long)height * 3)
        {
            diagnostics.Add(new("candidate.file_metadata", path + ".artifact", "Recorded byte length and 3:2 PNG dimensions must match the file."));
        }

        if (!string.Equals(Value(artifact, "sha256"), Sha256(bytes), StringComparison.Ordinal))
        {
            diagnostics.Add(new("candidate.file_hash", path + ".artifact.sha256", "Recorded SHA-256 does not match the candidate file."));
        }
    }

    private static void ValidateMarkdown(
        JsonObject manifest,
        IReadOnlyList<JsonObject> candidates,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var contract = manifest["markdownContract"]?.AsObject();
        var relativePath = contract is null ? string.Empty : Value(contract, "relativePath");
        var resolved = ResolveRepositoryPath(repositoryRoot, relativePath);
        if (resolved is null || !File.Exists(resolved))
        {
            diagnostics.Add(new("candidate.markdown_contract", "$.markdownContract", "CX-517 human-readable review sheet is missing."));
            return;
        }

        var markdown = File.ReadAllText(resolved);
        if (candidates.Any(candidate =>
                !markdown.Contains(Value(candidate, "assetId"), StringComparison.Ordinal) ||
                !markdown.Contains(Path.GetFileName(Value(candidate, "outputPath")), StringComparison.Ordinal)))
        {
            diagnostics.Add(new("candidate.markdown_contract", "$.markdownContract", "Markdown must list every asset ID and output filename."));
        }
    }

    private static string? ResolveRepositoryPath(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var root = Path.GetFullPath(repositoryRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? resolved
            : null;
    }

    private static string[] Strings(JsonObject value, string property) =>
        value[property]?.AsArray().Select(node => node?.GetValue<string>() ?? string.Empty).ToArray() ?? [];

    private static string Value(JsonObject value, string property) =>
        value[property]?.GetValue<string>() ?? string.Empty;

    private static long Number(JsonObject value, string property) =>
        value[property]?.GetValue<long>() ?? 0;

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private sealed record AnchorContract(string Path);

    [GeneratedRegex(@"\.v[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedAssetId();

    [GeneratedRegex(@"_v[1-9][0-9]*\.png$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionedPngName();
}
