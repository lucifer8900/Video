using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static partial class VisualAnchorCorrectionValidator
{
    private static readonly HashSet<string> RequiredAvoidItems =
    [
        "no_text",
        "no_watermark",
        "no_actor_likeness",
        "no_copied_landmark",
        "no_qing_clothing",
        "no_nonuniform_stretch",
        "grand_majestic_environment",
        "no_head_body_distortion",
    ];

    private static readonly HashSet<string> RequiredContinuityLocks =
    [
        "stable_identity",
        "stable_prop_count",
        "grounded_contact",
        "shared_environment_light",
        "grand_majestic_environment",
        "human_proportions",
        "normal_lens_perspective",
        "no_head_body_distortion",
        "no_identity_reuse",
    ];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static IReadOnlyList<CatalogDiagnostic> Validate(
        string manifestPath,
        string catalogPath,
        string candidateRoot,
        string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-510 correction JSON was empty.");
        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))?.AsObject()
            ?? throw new InvalidDataException("Reference catalog JSON was empty.");
        var catalogAssets = catalog["assets"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .Where(asset => asset["id"] is not null)
            .ToDictionary(asset => Value(asset, "id"), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        ValidatePolicy(manifest, diagnostics);
        var candidate = manifest["candidate"]?.AsObject();
        if (candidate is null)
        {
            diagnostics.Add(new("candidate.missing", "$.candidate", "CX-510 requires one correction candidate."));
            return diagnostics;
        }

        ValidateCandidate(candidate, catalogAssets, Path.GetFullPath(candidateRoot), repositoryRoot, diagnostics);
        return diagnostics;
    }

    private static void ValidatePolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["generationPolicy"]?.AsObject();
        if (Value(manifest, "cardId") != "CX-510" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            Value(manifest, "baseAssetId") != "identity.celadon_scout.v2" ||
            policy is null ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["geminiOrVeoUsed"]?.GetValue<bool>() != false ||
            policy["overwriteExistingAssets"]?.GetValue<bool>() != false ||
            policy["realPeopleIdentityReuseAllowed"]?.GetValue<bool>() != false ||
            policy["adoptionRequiresHumanReview"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("policy.fail_closed", "$.generationPolicy", "CX-510 must remain a non-overwriting, still-only, non-Veo human-review correction of v2."));
        }
    }

    private static void ValidateCandidate(
        JsonObject candidate,
        IReadOnlyDictionary<string, JsonObject> catalogAssets,
        string candidateRoot,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var path = "$.candidate";
        var assetId = Value(candidate, "assetId");
        var outputPath = Value(candidate, "outputPath");
        if (assetId != "identity.celadon_scout.v3" || !VersionedAssetId().IsMatch(assetId))
        {
            diagnostics.Add(new("candidate.identity", path + ".assetId", "CX-510 must publish only the versioned identity.celadon_scout.v3 correction."));
        }

        if (Value(candidate, "assetKind") != "identity_anchor" || Value(candidate, "generationMode") != "codex_builtin_imagegen")
        {
            diagnostics.Add(new("candidate.identity", path, "CX-510 correction must be a built-in ImageGen identity anchor."));
        }

        if (Value(candidate, "adoptionStatus") != "needs_review" ||
            candidate["adopted"]?.GetValue<bool>() != false ||
            candidate["shipInBuild"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("candidate.not_review_only", path, "CX-510 correction must remain needs_review, unadopted and excluded from builds."));
        }

        var normalizedOutput = outputPath.Replace('\\', '/');
        if (normalizedOutput != "content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png" ||
            normalizedOutput.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
            normalizedOutput.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
            normalizedOutput.StartsWith("unity/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new("candidate.runtime_path", path + ".outputPath", "CX-510 output must remain outside Unity runtime directories and use the v3 path."));
        }

        ValidateReferences(candidate, catalogAssets, path, diagnostics);
        ValidateResearch(candidate, repositoryRoot, path, diagnostics);
        ValidateProportionContract(candidate, path, diagnostics);
        ValidatePostProcess(candidate, path, diagnostics);
        ValidateArtifact(candidate, outputPath, candidateRoot, repositoryRoot, path, diagnostics);
    }

    private static void ValidateReferences(
        JsonObject candidate,
        IReadOnlyDictionary<string, JsonObject> catalogAssets,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var inputs = candidate["referenceInputs"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        var requiredRoles = new[] { "structure", "material", "lighting", "pose", "garment_construction" };
        var roles = inputs.Select(input => Value(input, "role")).ToHashSet(StringComparer.Ordinal);
        foreach (var role in requiredRoles.Where(role => !roles.Contains(role)))
        {
            diagnostics.Add(new("reference.role_missing", path + ".referenceInputs", $"Required reference role '{role}' is missing."));
        }

        foreach (var (input, index) in inputs.Select((input, index) => (input, index)))
        {
            var inputPath = $"{path}.referenceInputs[{index}]";
            var id = Value(input, "assetId");
            if (!catalogAssets.TryGetValue(id, out var reference))
            {
                diagnostics.Add(new("reference.unknown", inputPath, $"Reference '{id}' is not in the catalog."));
                continue;
            }
            if (Value(input, "assetSha256") != Value(reference, "sha256"))
            {
                diagnostics.Add(new("reference.hash_mismatch", inputPath, $"Reference '{id}' hash does not match the catalog."));
            }
            var catalogRoles = reference["referenceRoles"]?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal) ?? [];
            if (!catalogRoles.Contains(Value(input, "role")))
            {
                diagnostics.Add(new("reference.role_missing", inputPath, $"Reference '{id}' is not catalogued for the requested role."));
            }
            if (Value(reference, "category") == "people" && reference["identityReuse"]?.GetValue<bool>() != false)
            {
                diagnostics.Add(new("reference.identity_reuse", inputPath, "People references must prohibit identity reuse."));
            }
        }
    }

    private static void ValidateResearch(JsonObject candidate, string repositoryRoot, string path, List<CatalogDiagnostic> diagnostics)
    {
        var refs = candidate["researchReferences"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        if (refs.Length == 0)
        {
            diagnostics.Add(new("research.reference_missing", path + ".researchReferences", "CX-510 requires a private garment research reference."));
        }
        var privateRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "content", "reference-library", "private-research", "cx509"));
        foreach (var (reference, index) in refs.Select((reference, index) => (reference, index)))
        {
            var referencePath = $"{path}.researchReferences[{index}]";
            if (reference["identityReuse"]?.GetValue<bool>() != false)
            {
                diagnostics.Add(new("reference.identity_reuse", referencePath, "Research references must prohibit identity reuse."));
            }
            var relative = Value(reference, "localRelativePath").Replace('/', Path.DirectorySeparatorChar);
            var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            if (!resolved.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                diagnostics.Add(new("research.file_missing", referencePath, "Research file is missing or outside private-research/cx509."));
            }
        }
    }

    private static void ValidateProportionContract(JsonObject candidate, string path, List<CatalogDiagnostic> diagnostics)
    {
        var prompt = Value(candidate, "prompt");
        var avoid = candidate["avoidItems"]?.AsArray().Select(node => node?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal) ?? [];
        var locks = candidate["continuityLocks"]?.AsArray().Select(node => node?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal) ?? [];
        var promptRules = new[] { "7.5 heads", "50mm", "no wide-angle distortion", "head must not be enlarged" };
        if (prompt.Length < 350 || !promptRules.All(rule => prompt.Contains(rule, StringComparison.OrdinalIgnoreCase)) ||
            !RequiredAvoidItems.IsSubsetOf(avoid) || !RequiredContinuityLocks.IsSubsetOf(locks))
        {
            diagnostics.Add(new("candidate.proportion_contract", path, "The correction must lock 7.5-head anatomy, normal-lens perspective and no head/body distortion."));
        }
    }

    private static void ValidatePostProcess(JsonObject candidate, string path, List<CatalogDiagnostic> diagnostics)
    {
        var post = candidate["postProcess"]?.AsObject();
        if (post is null || Value(post, "mode") != "aspect_crop" || Number(post, "sourceWidth") != 1672 || Number(post, "sourceHeight") != 941 || Number(post, "outputWidth") != 1664 || Number(post, "outputHeight") != 936 || Value(post, "cropStrategy") != "center")
        {
            diagnostics.Add(new("candidate.non_uniform_scale", path + ".postProcess", "CX-510 must use the declared aspect-preserving center crop."));
        }
    }

    private static void ValidateArtifact(JsonObject candidate, string outputPath, string candidateRoot, string repositoryRoot, string path, List<CatalogDiagnostic> diagnostics)
    {
        var platformPath = outputPath.Replace('/', Path.DirectorySeparatorChar);
        var fromCandidateRoot = Path.GetFullPath(Path.Combine(candidateRoot, platformPath));
        var fromRepository = Path.GetFullPath(Path.Combine(repositoryRoot, platformPath));
        var resolved = fromCandidateRoot.StartsWith(candidateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fromCandidateRoot)
            ? fromCandidateRoot
            : fromRepository.StartsWith(candidateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fromRepository) ? fromRepository : null;
        if (resolved is null)
        {
            diagnostics.Add(new("candidate.file_missing", path + ".outputPath", "CX-510 candidate file is missing or outside the declared root."));
            return;
        }
        var bytes = File.ReadAllBytes(resolved);
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            diagnostics.Add(new("candidate.png_signature", path + ".artifact", "CX-510 candidate must be a PNG with a valid IHDR."));
            return;
        }
        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (width != 1664 || height != 936 || (long)width * 9 != (long)height * 16)
        {
            diagnostics.Add(new("candidate.aspect_ratio", path + ".artifact", $"CX-510 candidate dimensions {width}x{height} must be exact 1664x936."));
        }
        var artifact = candidate["artifact"]?.AsObject();
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (artifact is null || Value(artifact, "mediaType") != "image/png" || Number(artifact, "byteLength") != bytes.LongLength || Value(artifact, "sha256") != actualHash || Number(artifact, "width") != width || Number(artifact, "height") != height)
        {
            diagnostics.Add(new("candidate.artifact_mismatch", path + ".artifact", "CX-510 artifact metadata does not match the file."));
        }
    }

    private static string Value(JsonObject value, string property) => value[property]?.GetValue<string>() ?? string.Empty;
    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;
    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    [GeneratedRegex(@"\.v[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedAssetId();
}
