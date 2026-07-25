using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static partial class ScoutFirstFrameAdoptionValidator
{
    private const string SourceManifest = "content/story/red-mist/red-mist-scout-first-frame-propagation.cx512.json";
    private const string PromptOverride = "content/story/red-mist/video-generation-prompts.cx513.md";
    private const string CatalogPath = "unity/RedMistVerticalSlice/Assets/Scripts/Presentation/GeneratedArtCatalog.cs";
    private const string BuilderPath = "unity/RedMistVerticalSlice/Assets/Editor/VerticalSliceBuilder.cs";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly IReadOnlyDictionary<string, Expectation> Expected =
        new Dictionary<string, Expectation>(StringComparer.Ordinal)
        {
            ["scout_mist"] = new(
                "firstframe.dialogue.scout.mist.v4",
                "content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_mist_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png.meta",
                "Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4",
                "ScoutMistFirstFrame",
                "f46812de4e5179141ad442d5d35d8ff0",
                ["npc.response.prologue.inspect_mist", "npc.invalid.calm.abuse", "npc.invalid.calm.irrelevant", "npc.invalid.calm.too_long", "npc.invalid.calm.silence", "npc.invalid.calm.low_confidence"]),
            ["scout_alliance"] = new(
                "firstframe.dialogue.scout.alliance.v4",
                "content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_alliance_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png.meta",
                "Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4",
                "ScoutAllianceFirstFrame",
                "e7d82dfe40072f74a99cd91277e72ada",
                ["npc.response.alliance.cautious_cooperation", "npc.invalid.ally.abuse", "npc.invalid.ally.irrelevant", "npc.invalid.ally.too_long", "npc.invalid.ally.silence", "npc.invalid.ally.low_confidence"]),
            ["scout_rescue"] = new(
                "firstframe.dialogue.scout.rescue.v4",
                "content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_rescue_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_v4.png",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_v4.png.meta",
                "Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_v4",
                "ScoutRescueFirstFrame",
                "5909fd97b958c92409e1d5cdd8325bb9",
                ["npc.response.rescue.secure_survivor"]),
        };

    public static IReadOnlyList<CatalogDiagnostic> Validate(string manifestPath, string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-513 scout first-frame adoption JSON was empty.");
        var root = Path.GetFullPath(repositoryRoot);

        ValidatePolicy(manifest, root, diagnostics);
        ValidateSourceManifest(root, diagnostics);
        ValidatePromptOverride(manifest, root, diagnostics);

        var candidates = manifest["candidates"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        if (candidates.Length != Expected.Count || candidates.Select(candidate => Value(candidate, "sceneKey")).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
        {
            diagnostics.Add(new("coverage.candidates", "$.candidates", "CX-513 requires exactly one adopted candidate for mist, alliance and rescue."));
        }

        foreach (var (candidate, index) in candidates.Select((candidate, index) => (candidate, index)))
        {
            ValidateCandidate(candidate, index, root, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidatePolicy(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var review = manifest["humanReview"]?.AsObject();
        var policy = manifest["generationPolicy"]?.AsObject();
        if (Value(manifest, "cardId") != "CX-513" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            Value(manifest, "sourceCardId") != "CX-512" ||
            review is null || review["approved"]?.GetValue<bool>() != true ||
            Value(review, "decision") != "user_explicit" ||
            Value(review, "scope") != "three_celadon_scout_dialogue_first_frames_only" ||
            policy is null ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["geminiOrVeoUsed"]?.GetValue<bool>() != false ||
            policy["overwriteExistingAssets"]?.GetValue<bool>() != false ||
            policy["realPeopleIdentityReuseAllowed"]?.GetValue<bool>() != false ||
            policy["sourceCandidatesPreserved"]?.GetValue<bool>() != true ||
            policy["grandV2SourcesPreserved"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("approval.required", "$.humanReview", "CX-513 requires explicit user approval for only the three scout first frames and preserves all source history."));
        }

        if (Value(manifest, "promptOverridePath") != PromptOverride)
        {
            diagnostics.Add(new("prompt.override_missing", "$.promptOverridePath", "The adopted v4 upload paths must be recorded in the CX-513 prompt override."));
        }

        var sourceCardPath = Resolve(repositoryRoot, SourceManifest);
        if (!File.Exists(sourceCardPath))
        {
            diagnostics.Add(new("source.provenance_missing", "$.sourceCardId", "The CX-512 review-only provenance manifest must remain checked in."));
        }
    }

    private static void ValidateSourceManifest(string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var path = Resolve(repositoryRoot, SourceManifest);
        if (!File.Exists(path)) return;
        var source = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        if (Value(source, "cardId") != "CX-512")
        {
            diagnostics.Add(new("source.provenance_mutated", "$.sourceCardId", "CX-513 cannot adopt from a rewritten source card."));
        }

        var candidates = source?["candidates"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        foreach (var candidate in candidates)
        {
            if (Value(candidate, "adoptionStatus") != "needs_review" || candidate["adopted"]?.GetValue<bool>() != false || candidate["shipInBuild"]?.GetValue<bool>() != false)
            {
                diagnostics.Add(new("source.provenance_mutated", "$.sourceCardId", "CX-512 must remain an immutable review-only source record."));
                break;
            }
        }
    }

    private static void ValidatePromptOverride(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var path = Resolve(repositoryRoot, PromptOverride);
        if (!File.Exists(path))
        {
            diagnostics.Add(new("prompt.override_missing", "$.promptOverridePath", "The CX-513 prompt override file is missing."));
            return;
        }

        var text = File.ReadAllText(path);
        if (!text.Contains("firstframe_dialogue_scout_mist_v4.png", StringComparison.Ordinal) ||
            !text.Contains("firstframe_dialogue_scout_alliance_v4.png", StringComparison.Ordinal) ||
            !text.Contains("firstframe_dialogue_scout_rescue_v4.png", StringComparison.Ordinal) ||
            !text.Contains("npc.invalid.calm.low_confidence", StringComparison.Ordinal) ||
            !text.Contains("npc.invalid.ally.low_confidence", StringComparison.Ordinal))
        {
            diagnostics.Add(new("prompt.mapping", "$.promptOverridePath", "The CX-513 prompt override must map all three v4 frames and their five-class NPC response reuse."));
        }
    }

    private static void ValidateCandidate(JsonObject candidate, int index, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.candidates[{index}]";
        var sceneKey = Value(candidate, "sceneKey");
        if (!Expected.TryGetValue(sceneKey, out var expected))
        {
            diagnostics.Add(new("coverage.candidates", path + ".sceneKey", "Unknown CX-513 scout scene."));
            return;
        }

        if (Value(candidate, "assetId") != expected.AssetId)
            diagnostics.Add(new("coverage.candidates", path + ".assetId", "Adopted asset ID does not match its scene."));
        if (Value(candidate, "sourcePath") != expected.SourcePath)
            diagnostics.Add(new("source.path", path + ".sourcePath", "Adoption must use the exact approved CX-512 candidate."));
        if (Value(candidate, "adoptionStatus") != "adopted" || candidate["adopted"]?.GetValue<bool>() != true || candidate["shipInBuild"]?.GetValue<bool>() != true || Value(candidate, "generationMode") != "codex_builtin_imagegen")
            diagnostics.Add(new("adoption.required", path, "Approved CX-513 candidates must be adopted, shipped and remain attributable to the built-in still-image generation step."));
        if (candidate["overwriteExistingAssets"]?.GetValue<bool>() != false)
            diagnostics.Add(new("target.overwrite", path + ".overwriteExistingAssets", "CX-513 must never overwrite an existing Unity asset."));

        var sourcePath = Resolve(repositoryRoot, expected.SourcePath);
        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(new("source.file_missing", path + ".sourcePath", "The approved CX-512 candidate file is missing."));
        }
        else
        {
            ValidateArtifact(candidate["sourceArtifact"]?.AsObject(), File.ReadAllBytes(sourcePath), path + ".sourceArtifact", "source.hash_mismatch", diagnostics);
        }

        if (Value(candidate, "targetPath") != expected.TargetPath || !IsUnityVideoFirstFramePath(Value(candidate, "targetPath")))
            diagnostics.Add(new("target.runtime_path", path + ".targetPath", "Adopted first frames must use a new versioned path under Unity Resources/Generated/VideoFirstFrames."));
        if (Value(candidate, "targetMetaPath") != expected.TargetMetaPath || Value(candidate, "targetGuid") != expected.Guid)
            diagnostics.Add(new("target.meta_invalid", path + ".targetMetaPath", "The adopted Unity .meta path and GUID must be recorded exactly."));
        if (Value(candidate, "runtimeResourcePath") != expected.RuntimeResourcePath)
            diagnostics.Add(new("target.registration", path + ".runtimeResourcePath", "The Resources load key does not match the adopted v4 path."));
        if (Value(candidate, "registryConstant") != expected.RegistryConstant)
            diagnostics.Add(new("target.registration", path + ".registryConstant", "The GeneratedArtCatalog constant does not match the adopted scene."));

        var responseIds = Strings(candidate["responseIds"]);
        if (!responseIds.SetEquals(expected.ResponseIds))
            diagnostics.Add(new("response.mapping", path + ".responseIds", "The adopted frame must cover the approved primary response and five-class exception responses for its scene."));

        var targetPath = Resolve(repositoryRoot, expected.TargetPath);
        var targetMetaPath = Resolve(repositoryRoot, expected.TargetMetaPath);
        if (!File.Exists(targetPath))
        {
            diagnostics.Add(new("target.file_missing", path + ".targetPath", "The adopted Unity first-frame PNG is missing."));
        }
        else
        {
            var targetBytes = File.ReadAllBytes(targetPath);
            ValidateArtifact(candidate["artifact"]?.AsObject(), targetBytes, path + ".artifact", "target.hash_mismatch", diagnostics);
            if (File.Exists(sourcePath) && !targetBytes.AsSpan().SequenceEqual(File.ReadAllBytes(sourcePath)))
                diagnostics.Add(new("target.source_drift", path + ".targetPath", "The Unity target must be an exact byte-for-byte copy of the approved candidate."));
        }

        if (!File.Exists(targetMetaPath))
        {
            diagnostics.Add(new("target.meta_invalid", path + ".targetMetaPath", "The adopted Unity first-frame PNG must have a checked-in .meta file."));
        }
        else
        {
            var meta = File.ReadAllText(targetMetaPath);
            var match = GuidLine().Match(meta);
            if (!match.Success || !string.Equals(match.Groups[1].Value, expected.Guid, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new("target.meta_invalid", path + ".targetMetaPath", "The Unity .meta GUID does not match the adopted manifest."));
        }

        var catalog = Resolve(repositoryRoot, CatalogPath);
        var builder = Resolve(repositoryRoot, BuilderPath);
        if (!File.Exists(catalog) || !File.ReadAllText(catalog).Contains($"{expected.RegistryConstant} = \"{expected.RuntimeResourcePath}\"", StringComparison.Ordinal))
            diagnostics.Add(new("target.registration", path + ".registryConstant", "GeneratedArtCatalog must register the adopted v4 Resources key."));
        if (!File.Exists(builder) || !File.ReadAllText(builder).Contains($"GeneratedArtCatalog.{expected.RegistryConstant}", StringComparison.Ordinal))
            diagnostics.Add(new("target.registration", path + ".registryConstant", "VerticalSliceBuilder must validate the adopted v4 asset."));
    }

    private static void ValidateArtifact(JsonObject? artifact, byte[] bytes, string path, string code, List<CatalogDiagnostic> diagnostics)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            diagnostics.Add(new(code, path, "Adopted artifact must be a PNG with a valid IHDR."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (artifact is null || Value(artifact, "mediaType") != "image/png" || Number(artifact, "byteLength") != bytes.LongLength ||
            Value(artifact, "sha256") != hash || Number(artifact, "width") != width || Number(artifact, "height") != height ||
            width != 1664 || height != 936)
        {
            diagnostics.Add(new(code, path, "Adopted artifact metadata, hash or dimensions do not match the file."));
        }
    }

    private static bool IsUnityVideoFirstFramePath(string path) =>
        path.Replace('\\', '/').StartsWith("unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_", StringComparison.Ordinal) &&
        path.EndsWith("_v4.png", StringComparison.Ordinal);

    private static HashSet<string> Strings(JsonNode? value) => value?.AsArray()
        .Select(node => node?.GetValue<string>() ?? string.Empty)
        .ToHashSet(StringComparer.Ordinal) ?? [];
    private static string Resolve(string root, string relative) => Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    private static string Value(JsonObject? value, string property) => value?[property]?.GetValue<string>() ?? string.Empty;
    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;
    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    [GeneratedRegex(@"(?m)^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GuidLine();

    private sealed record Expectation(string AssetId, string SourcePath, string TargetPath, string TargetMetaPath, string RuntimeResourcePath, string RegistryConstant, string Guid, string[] ResponseIds);
}
