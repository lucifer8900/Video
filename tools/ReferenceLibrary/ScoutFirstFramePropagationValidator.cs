using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ScoutFirstFramePropagationValidator
{
    private const string ExpectedIdentity = "unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/identity_celadon_scout_v3.png";
    private const string CandidateRoot = "content/visual-candidates/cx512/first-frames";
    private const string ContactSheet = "content/visual-candidates/cx512/reports/contact-sheet.cx512.png";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly HashSet<string> RequiredAvoidItems =
    [
        "no_text", "no_watermark", "no_actor_likeness", "no_copied_landmark", "no_qing_clothing",
        "no_modern_objects", "no_handheld_tablet", "no_extra_foreground_people", "no_duplicate_talisman",
        "no_floating_feet", "no_stretched_anatomy", "no_costume_drift",
    ];
    private static readonly HashSet<string> RequiredContinuityLocks =
    [
        "stable_v3_identity", "stable_face_and_hair", "stable_proportions", "stable_v3_costume",
        "one_pinned_talisman", "hands_empty", "one_foreground_scout", "shared_scene_geometry",
        "shared_environment_light", "grounded_contact", "normal_lens_perspective",
        "old_grand_v2_preserved_only_as_scene_reference",
    ];
    private static readonly IReadOnlyDictionary<string, SourceExpectation> ExpectedSources =
        new Dictionary<string, SourceExpectation>(StringComparer.Ordinal)
        {
            ["scout_mist"] = new(
                "firstframe.dialogue.scout.mist.v4",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png",
                2297466,
                "30bd39fd642eea47aa8e07b8f97bbf30ca7d12bef97236ba45b43eb16c46c45a"),
            ["scout_alliance"] = new(
                "firstframe.dialogue.scout.alliance.v4",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png",
                2679698,
                "df4e1851789f97a84ec241b728a33f4161399d073eb607fb6b0c362a022129ce"),
            ["scout_rescue"] = new(
                "firstframe.dialogue.scout.rescue.v4",
                "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_grand_v2.png",
                2410905,
                "77d8080b786caaa7ad948fa0bb2098c4cb75d9eeaa9344e6bcbf0b0e65404319"),
        };

    public static IReadOnlyList<CatalogDiagnostic> Validate(string manifestPath, string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-512 scout first-frame propagation JSON was empty.");
        var root = Path.GetFullPath(repositoryRoot);

        ValidatePolicy(manifest, root, diagnostics);
        ValidateIdentityReference(manifest, root, diagnostics);
        ValidateContactSheet(manifest, root, diagnostics);

        var candidates = manifest["candidates"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        ValidateCoverage(candidates, diagnostics);
        foreach (var (candidate, index) in candidates.Select((candidate, index) => (candidate, index)))
        {
            ValidateCandidate(candidate, index, root, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidatePolicy(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["generationPolicy"]?.AsObject();
        if (Value(manifest, "cardId") != "CX-512" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            Value(manifest, "baseIdentityAssetId") != "identity.celadon_scout.v3" ||
            manifest["sourceFramesPreserved"]?.GetValue<bool>() != true ||
            policy is null ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["geminiOrVeoUsed"]?.GetValue<bool>() != false ||
            policy["overwriteExistingAssets"]?.GetValue<bool>() != false ||
            policy["realPeopleIdentityReuseAllowed"]?.GetValue<bool>() != false ||
            policy["adoptionRequiresHumanReview"]?.GetValue<bool>() != true ||
            policy["existingFirstFramesPreserved"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("policy.fail_closed", "$.generationPolicy", "CX-512 is still-image-only, non-overwriting, non-Veo and requires human review."));
        }

        var markdown = Path.Combine(repositoryRoot, "content", "story", "red-mist", "red-mist-scout-first-frame-propagation.cx512.md");
        if (!File.Exists(markdown))
        {
            diagnostics.Add(new("provenance.readme_missing", "$.cardId", "CX-512 must include a review note explaining the v3 identity propagation and preserved v2 sources."));
        }
    }

    private static void ValidateIdentityReference(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var reference = manifest["identityReference"]?.AsObject();
        if (reference is null || Value(reference, "path") != ExpectedIdentity)
        {
            diagnostics.Add(new("reference.identity_path", "$.identityReference.path", "The authoritative identity reference must be the adopted Unity celadon scout v3 asset."));
            return;
        }

        var resolved = Resolve(repositoryRoot, ExpectedIdentity);
        if (!File.Exists(resolved))
        {
            diagnostics.Add(new("reference.identity_missing", "$.identityReference.path", "The adopted v3 identity reference is missing."));
            return;
        }

        ValidateArtifact(reference, File.ReadAllBytes(resolved), "$.identityReference", 1664, 936, "reference.hash_mismatch", diagnostics);
    }

    private static void ValidateContactSheet(JsonObject manifest, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var reference = manifest["comparisonContactSheet"]?.AsObject();
        if (reference is null || Value(reference, "path") != ContactSheet)
        {
            diagnostics.Add(new("coverage.contact_sheet", "$.comparisonContactSheet.path", "CX-512 must record the old-v2 versus new-v4 comparison contact sheet."));
            return;
        }

        var resolved = Resolve(repositoryRoot, ContactSheet);
        if (!File.Exists(resolved))
        {
            diagnostics.Add(new("coverage.contact_sheet", "$.comparisonContactSheet.path", "The CX-512 comparison contact sheet is missing."));
            return;
        }

        ValidateArtifact(reference, File.ReadAllBytes(resolved), "$.comparisonContactSheet", 1536, 1452, "coverage.contact_sheet", diagnostics);
    }

    private static void ValidateCoverage(IReadOnlyList<JsonObject> candidates, List<CatalogDiagnostic> diagnostics)
    {
        var expected = ExpectedSources.Values.Select(source => source.AssetId).ToHashSet(StringComparer.Ordinal);
        var actual = candidates.Select(candidate => Value(candidate, "assetId")).ToArray();
        if (candidates.Count != ExpectedSources.Count || actual.Distinct(StringComparer.Ordinal).Count() != candidates.Count || actual.Any(assetId => !expected.Contains(assetId)))
        {
            diagnostics.Add(new("coverage.candidates", "$.candidates", "CX-512 requires exactly one v4 review candidate for mist, alliance and rescue."));
        }
    }

    private static void ValidateCandidate(JsonObject candidate, int index, string repositoryRoot, List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.candidates[{index}]";
        var sceneKey = Value(candidate, "sceneKey");
        if (!ExpectedSources.TryGetValue(sceneKey, out var expected))
        {
            diagnostics.Add(new("coverage.candidates", path + ".sceneKey", "Unknown CX-512 scout dialogue scene."));
            return;
        }

        if (Value(candidate, "assetId") != expected.AssetId)
        {
            diagnostics.Add(new("coverage.candidates", path + ".assetId", $"Scene {sceneKey} must use {expected.AssetId}."));
        }

        if (Value(candidate, "adoptionStatus") != "needs_review" ||
            candidate["adopted"]?.GetValue<bool>() != false ||
            candidate["shipInBuild"]?.GetValue<bool>() != false ||
            Value(candidate, "generationMode") != "codex_builtin_imagegen")
        {
            diagnostics.Add(new("candidate.not_review_only", path, "CX-512 candidates must remain needs_review, unadopted, excluded from builds and still-only."));
        }

        var outputPath = Value(candidate, "outputPath").Replace('\\', '/');
        var expectedOutput = $"{CandidateRoot}/firstframe_dialogue_scout_{sceneKey.Replace("scout_", string.Empty, StringComparison.Ordinal)}_v4.png";
        if (outputPath != expectedOutput || outputPath.StartsWith("unity/", StringComparison.OrdinalIgnoreCase) ||
            outputPath.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) || outputPath.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
            !outputPath.StartsWith(CandidateRoot + "/", StringComparison.Ordinal))
        {
            diagnostics.Add(new("candidate.runtime_path", path + ".outputPath", "CX-512 candidate images must remain in content/visual-candidates/cx512/first-frames, never Unity runtime folders."));
        }

        var identity = candidate["identityReference"]?.AsObject();
        if (identity is null || Value(identity, "path") != ExpectedIdentity)
        {
            diagnostics.Add(new("reference.identity_path", path + ".identityReference.path", "Each candidate must use the adopted celadon scout v3 identity reference."));
        }

        var sourcePath = Value(candidate, "sourceFirstFramePath").Replace('\\', '/');
        if (sourcePath != expected.SourcePath)
        {
            diagnostics.Add(new("reference.source_path", path + ".sourceFirstFramePath", "The legacy grand v2 first frame path is the scene-composition provenance and must not drift."));
        }
        ValidateSource(candidate, expected, repositoryRoot, path, diagnostics);

        var avoid = Strings(candidate["avoidItems"]);
        var locks = Strings(candidate["continuityLocks"]);
        var prompt = Value(candidate, "prompt");
        var promptRules = new[] { "v3", "65mm", "no wide-angle distortion", "mouth naturally closed", "hands are empty" };
        if (prompt.Length < 500 || promptRules.Any(rule => !prompt.Contains(rule, StringComparison.OrdinalIgnoreCase)) ||
            !RequiredAvoidItems.IsSubsetOf(avoid) || !RequiredContinuityLocks.IsSubsetOf(locks))
        {
            diagnostics.Add(new("candidate.continuity_contract", path, "Prompt, avoid list and continuity locks must preserve v3 identity, normal perspective, grounded anatomy and the shared scene contract."));
        }

        var post = candidate["postProcess"]?.AsObject();
        if (post is null || Value(post, "mode") != "aspect_crop" || Number(post, "sourceWidth") != 1672 ||
            Number(post, "sourceHeight") != 941 || Number(post, "outputWidth") != 1664 || Number(post, "outputHeight") != 936 ||
            Value(post, "cropStrategy") != "center")
        {
            diagnostics.Add(new("candidate.non_uniform_scale", path + ".postProcess", "CX-512 must record the declared 1672x941 to 1664x936 center crop."));
        }

        var output = Resolve(repositoryRoot, outputPath);
        if (!File.Exists(output))
        {
            diagnostics.Add(new("candidate.file_missing", path + ".outputPath", "CX-512 candidate image is missing."));
        }
        else
        {
            ValidateArtifact(candidate["artifact"]?.AsObject(), File.ReadAllBytes(output), path + ".artifact", 1664, 936, "candidate.artifact_mismatch", diagnostics);
        }
    }

    private static void ValidateSource(JsonObject candidate, SourceExpectation expected, string repositoryRoot, string path, List<CatalogDiagnostic> diagnostics)
    {
        var source = candidate["sourceFirstFrame"]?.AsObject();
        if (source is null || Value(source, "path") != expected.SourcePath)
        {
            diagnostics.Add(new("reference.source_path", path + ".sourceFirstFrame.path", "Source metadata must point to the immutable grand v2 frame."));
            return;
        }

        var resolved = Resolve(repositoryRoot, expected.SourcePath);
        if (!File.Exists(resolved))
        {
            diagnostics.Add(new("reference.source_missing", path + ".sourceFirstFrame.path", "The preserved grand v2 source frame is missing."));
            return;
        }

        ValidateArtifact(source, File.ReadAllBytes(resolved), path + ".sourceFirstFrame", 1672, 941, "reference.hash_mismatch", diagnostics);
    }

    private static void ValidateArtifact(JsonObject? artifact, byte[] bytes, string path, int expectedWidth, int expectedHeight, string code, List<CatalogDiagnostic> diagnostics)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            diagnostics.Add(new(code, path, "Artifact must be a PNG with a valid IHDR."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (artifact is null || Value(artifact, "mediaType") != "image/png" || Number(artifact, "byteLength") != bytes.LongLength ||
            Value(artifact, "sha256") != hash || Number(artifact, "width") != width || Number(artifact, "height") != height ||
            width != expectedWidth || height != expectedHeight)
        {
            diagnostics.Add(new(code, path, $"Artifact metadata or dimensions do not match the immutable file; expected {expectedWidth}x{expectedHeight}."));
        }
    }

    private static HashSet<string> Strings(JsonNode? value) => value?.AsArray()
        .Select(node => node?.GetValue<string>() ?? string.Empty)
        .ToHashSet(StringComparer.Ordinal) ?? [];

    private static string Resolve(string repositoryRoot, string relative) =>
        Path.GetFullPath(Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Value(JsonObject? value, string property) => value?[property]?.GetValue<string>() ?? string.Empty;
    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;
    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private sealed record SourceExpectation(string AssetId, string SourcePath, long ByteLength, string Sha256);
}
