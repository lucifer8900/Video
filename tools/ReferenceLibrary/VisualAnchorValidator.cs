using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static partial class VisualAnchorValidator
{
    private static readonly string[] RequiredAnchorIds =
    [
        "identity.chu_mingqi.v2",
        "identity.celadon_scout.v2",
        "identity.formation_spirit.v2",
        "identity.shi_jun.v3",
        "creature.ink_dragon.v3",
    ];

    private static readonly HashSet<string> RequiredAvoidItems =
    [
        "no_text",
        "no_watermark",
        "no_actor_likeness",
        "no_copied_landmark",
        "no_qing_clothing",
        "no_nonuniform_stretch",
        "grand_majestic_environment",
        "no_lizard_like",
    ];

    private static readonly HashSet<string> RequiredContinuityLocks =
    [
        "stable_identity",
        "stable_prop_count",
        "grounded_contact",
        "shared_environment_light",
        "grand_majestic_environment",
        "human_proportions",
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
            ?? throw new InvalidDataException("CX-509 visual anchor JSON was empty.");
        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))?.AsObject()
            ?? throw new InvalidDataException("Reference catalog JSON was empty.");
        var catalogAssets = catalog["assets"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .Where(asset => asset["id"] is not null)
            .ToDictionary(asset => Value(asset, "id"), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        ValidateGenerationPolicy(manifest, diagnostics);
        var candidates = manifest["candidates"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        ValidateCoverage(candidates, diagnostics);

        var allowedRoot = Path.GetFullPath(candidateRoot);
        foreach (var (candidate, index) in candidates.Select((candidate, index) => (candidate, index)))
        {
            ValidateCandidate(candidate, index, catalogAssets, allowedRoot, repositoryRoot, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateGenerationPolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["generationPolicy"]?.AsObject();
        if (Value(manifest, "cardId") != "CX-509" ||
            policy is null ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            policy["geminiOrVeoUsed"]?.GetValue<bool>() != false ||
            policy["overwriteExistingAssets"]?.GetValue<bool>() != false ||
            policy["realPeopleIdentityReuseAllowed"]?.GetValue<bool>() != false ||
            policy["adoptionRequiresHumanReview"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("policy.fail_closed", "$.generationPolicy", "CX-509 must remain still-only, non-overwriting, non-Veo and human-reviewed."));
        }
    }

    private static void ValidateCoverage(IReadOnlyList<JsonObject> candidates, List<CatalogDiagnostic> diagnostics)
    {
        var declared = candidates.Select(candidate => Value(candidate, "assetId")).ToArray();
        if (!declared.SequenceEqual(RequiredAnchorIds, StringComparer.Ordinal))
        {
            diagnostics.Add(new("coverage.identity_anchors", "$.candidates", "CX-509 requires the ordered five replacement anchors."));
        }
    }

    private static void ValidateCandidate(
        JsonObject candidate,
        int index,
        IReadOnlyDictionary<string, JsonObject> catalogAssets,
        string allowedRoot,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.candidates[{index}]";
        var assetId = Value(candidate, "assetId");
        var kind = Value(candidate, "assetKind");
        var outputPath = Value(candidate, "outputPath");

        if (!VersionedAssetId().IsMatch(assetId) || !VersionedPngName().IsMatch(Path.GetFileName(outputPath)))
        {
            diagnostics.Add(new("candidate.versioned_name", path, "CX-509 anchor IDs and PNG filenames must end in an explicit vN version."));
        }

        if (Value(candidate, "adoptionStatus") != "needs_review" ||
            candidate["adopted"]?.GetValue<bool>() != false ||
            candidate["shipInBuild"]?.GetValue<bool>() != false ||
            Value(candidate, "generationMode") != "codex_builtin_imagegen")
        {
            diagnostics.Add(new("candidate.not_review_only", path, "Every CX-509 candidate must remain unadopted, needs_review and excluded from builds."));
        }

        var normalizedOutput = outputPath.Replace('\\', '/');
        if (!normalizedOutput.StartsWith("content/visual-candidates/cx509/", StringComparison.Ordinal) ||
            normalizedOutput.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
            normalizedOutput.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
            normalizedOutput.StartsWith("unity/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new("candidate.runtime_path", path + ".outputPath", "CX-509 candidates must stay outside Unity runtime asset directories."));
        }

        ValidateReferences(candidate, kind, catalogAssets, path, diagnostics);
        ValidateResearchReferences(candidate, path, repositoryRoot, diagnostics);
        ValidatePromptContract(candidate, kind, path, diagnostics);
        ValidatePostProcess(candidate, path, diagnostics);
        ValidateArtifact(candidate, outputPath, allowedRoot, repositoryRoot, path, diagnostics);
    }

    private static void ValidateReferences(
        JsonObject candidate,
        string kind,
        IReadOnlyDictionary<string, JsonObject> catalogAssets,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var inputs = candidate["referenceInputs"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        var requiredRoles = kind == "creature_anchor"
            ? new[] { "structure", "material", "lighting", "anatomy" }
            : new[] { "structure", "material", "lighting", "pose", "garment_construction" };
        var actualRoles = inputs.Select(input => Value(input, "role")).ToHashSet(StringComparer.Ordinal);
        foreach (var role in requiredRoles.Where(role => !actualRoles.Contains(role)))
        {
            diagnostics.Add(new("reference.role_missing", path + ".referenceInputs", $"Required reference role '{role}' is missing."));
        }

        foreach (var (input, inputIndex) in inputs.Select((input, inputIndex) => (input, inputIndex)))
        {
            var inputPath = $"{path}.referenceInputs[{inputIndex}]";
            var referenceId = Value(input, "assetId");
            if (!catalogAssets.TryGetValue(referenceId, out var reference))
            {
                diagnostics.Add(new("reference.unknown", inputPath + ".assetId", $"Reference '{referenceId}' is not in the catalog."));
                continue;
            }

            if (!string.Equals(Value(input, "assetSha256"), Value(reference, "sha256"), StringComparison.Ordinal))
            {
                diagnostics.Add(new("reference.hash_mismatch", inputPath + ".assetSha256", $"Reference '{referenceId}' hash does not match the catalog."));
            }

            var role = Value(input, "role");
            var catalogRoles = reference["referenceRoles"]?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal) ?? [];
            if (!catalogRoles.Contains(role))
            {
                diagnostics.Add(new("reference.role_missing", inputPath + ".role", $"Reference '{referenceId}' is not catalogued for role '{role}'."));
            }

            if (Value(reference, "category") == "people" && reference["identityReuse"]?.GetValue<bool>() != false)
            {
                diagnostics.Add(new("reference.identity_reuse", inputPath, "People references must explicitly prohibit identity reuse."));
            }

            if (Value(reference, "license") == "unverified" &&
                (reference["commercialUseAllowed"]?.GetValue<bool>() != false ||
                 reference["modificationsAllowed"]?.GetValue<bool>() != false ||
                 reference["referenceOnly"]?.GetValue<bool>() != true ||
                 reference["shipInBuild"]?.GetValue<bool>() != false))
            {
                diagnostics.Add(new("reference.unverified_rights", inputPath, "Unverified sources must remain personal reference-only and cannot claim commercial or modification rights."));
            }
        }
    }

    private static void ValidateResearchReferences(
        JsonObject candidate,
        string path,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var references = candidate["researchReferences"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => node!.AsObject())
            .ToArray() ?? [];
        if (references.Length == 0)
        {
            diagnostics.Add(new("research.reference_missing", path + ".researchReferences", "Each CX-509 candidate must retain at least one private web research reference."));
        }

        foreach (var (reference, index) in references.Select((reference, index) => (reference, index)))
        {
            var referencePath = $"{path}.researchReferences[{index}]";
            if (reference["identityReuse"]?.GetValue<bool>() != false)
            {
                diagnostics.Add(new("reference.identity_reuse", referencePath, "Private web references must explicitly prohibit identity reuse."));
            }

            var relative = Value(reference, "localRelativePath").Replace('/', Path.DirectorySeparatorChar);
            if (!relative.StartsWith(Path.Combine("content", "reference-library", "private-research", "cx509") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new("research.path_scope", referencePath + ".localRelativePath", "CX-509 research files must remain under private-research/cx509."));
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            var privateRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "content", "reference-library", "private-research", "cx509"));
            if (!resolved.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                diagnostics.Add(new("research.file_missing", referencePath + ".localRelativePath", "Private web research file is missing or outside the scoped research directory."));
            }
        }
    }

    private static void ValidatePromptContract(JsonObject candidate, string kind, string path, List<CatalogDiagnostic> diagnostics)
    {
        var prompt = Value(candidate, "prompt");
        var avoid = candidate["avoidItems"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var locks = candidate["continuityLocks"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        if (prompt.Length < 300 ||
            !RequiredAvoidItems.IsSubsetOf(avoid) ||
            !RequiredContinuityLocks.IsSubsetOf(locks))
        {
            diagnostics.Add(new("candidate.continuity_contract", path, "Prompt, avoid list and continuity locks must preserve grand environments, identity, props, grounding, lighting, text, anatomy and likeness boundaries."));
        }

        if (kind == "creature_anchor" &&
            (!prompt.Contains("墨蛟", StringComparison.Ordinal) ||
             !prompt.Contains("Chinese dragon", StringComparison.OrdinalIgnoreCase) ||
             !prompt.Contains("no lizard", StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new("candidate.creature_identity", path + ".prompt", "The 墨蛟 prompt must explicitly establish Chinese-dragon anatomy and reject lizard-like anatomy."));
        }
    }

    private static void ValidatePostProcess(JsonObject candidate, string path, List<CatalogDiagnostic> diagnostics)
    {
        var post = candidate["postProcess"]?.AsObject();
        if (post is null ||
            Value(post, "mode") != "aspect_crop" ||
            Number(post, "sourceWidth") != 1672 ||
            Number(post, "sourceHeight") != 941 ||
            Number(post, "outputWidth") != 1664 ||
            Number(post, "outputHeight") != 936 ||
            Value(post, "cropStrategy") != "center")
        {
            diagnostics.Add(new("candidate.non_uniform_scale", path + ".postProcess", "CX-509 output must use the declared 1672x941 to 1664x936 aspect-preserving center crop."));
        }
    }

    private static void ValidateArtifact(
        JsonObject candidate,
        string outputPath,
        string allowedRoot,
        string repositoryRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var resolved = ResolveOutputPath(outputPath, allowedRoot, repositoryRoot);
        if (resolved is null || !File.Exists(resolved))
        {
            diagnostics.Add(new("candidate.file_missing", path + ".outputPath", $"Candidate file is missing or outside the declared root: {outputPath}"));
            return;
        }

        var bytes = File.ReadAllBytes(resolved);
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            diagnostics.Add(new("candidate.png_signature", path + ".artifact", "Candidate must be a PNG with a valid signature and IHDR header."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (width != 1664 || height != 936 || (long)width * 9 != (long)height * 16)
        {
            diagnostics.Add(new("candidate.aspect_ratio", path + ".artifact", $"Candidate dimensions {width}x{height} must be exact 1664x936 16:9."));
        }

        var artifact = candidate["artifact"]?.AsObject();
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (artifact is null ||
            Value(artifact, "mediaType") != "image/png" ||
            Number(artifact, "byteLength") != bytes.LongLength ||
            !string.Equals(Value(artifact, "sha256"), actualHash, StringComparison.Ordinal) ||
            Number(artifact, "width") != width ||
            Number(artifact, "height") != height)
        {
            diagnostics.Add(new("candidate.artifact_mismatch", path + ".artifact", "Recorded media type, length, hash or dimensions do not match the file."));
        }
    }

    private static string? ResolveOutputPath(string outputPath, string allowedRoot, string repositoryRoot)
    {
        var platformPath = outputPath.Replace('/', Path.DirectorySeparatorChar);
        var fromAllowedRoot = Path.GetFullPath(Path.Combine(allowedRoot, platformPath));
        if (fromAllowedRoot.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fromAllowedRoot))
        {
            return fromAllowedRoot;
        }

        var fromRepository = Path.GetFullPath(Path.Combine(repositoryRoot, platformPath));
        return fromRepository.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fromRepository, allowedRoot, StringComparison.OrdinalIgnoreCase)
            ? fromRepository
            : null;
    }

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static string Value(JsonObject value, string property) => value[property]?.GetValue<string>() ?? string.Empty;

    private static long Number(JsonObject value, string property) => value[property]?.GetValue<long>() ?? 0;

    [GeneratedRegex(@"\.v[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedAssetId();

    [GeneratedRegex(@"_v[1-9][0-9]*\.png$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionedPngName();
}
