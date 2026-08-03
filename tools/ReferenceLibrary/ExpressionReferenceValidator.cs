using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ExpressionReferenceValidator
{
    private static readonly string[] RequiredSubjects =
    [
        "character.shen_yan",
        "character.chu_mingqi",
        "character.shi_jun",
        "character.celadon_scout",
        "character.cavern_ally",
        "character.formation_spirit",
    ];

    private static readonly string[] RequiredEmotions =
    [
        "neutral",
        "calm_warmth",
        "restrained_joy",
        "resolve",
        "vigilance",
        "suspicion",
        "concern",
        "grief",
        "anger_controlled",
        "fear_suppressed",
        "pain",
        "astonishment",
    ];

    private static readonly string[] RequiredGroups = ["a", "b", "c"];
    private static readonly string[] RequiredQuadrants = ["top_left", "top_right", "bottom_left", "bottom_right"];
    private static readonly string[] RequiredReviewChecks =
    [
        "identity_consistency",
        "expression_readability",
        "eye_anatomy",
        "skin_texture",
        "neckline_consistency",
        "no_text_or_watermark",
        "no_actor_likeness",
    ];

    private static readonly SubjectDefinition[] SubjectDefinitions =
    [
        new("character.shen_yan", "shen_yan", "identity.shen_yan.v1", "content/visual-candidates/cx508/identity-anchors/identity_shen_yan_v1.png", "turnaround.outfit.shen_yan_qinglan_travel.v1"),
        new("character.chu_mingqi", "chu_mingqi", "identity.chu_mingqi.v2", "content/visual-candidates/cx509/identity-anchors/identity_chu_mingqi_v2.png", "turnaround.outfit.chu_mingqi_jiyue_travel.v1"),
        new("character.shi_jun", "shi_jun", "identity.shi_jun.v3", "content/visual-candidates/cx509/identity-anchors/identity_shi_jun_v3.png", "turnaround.outfit.shi_jun_cangjin_command.v1"),
        new("character.celadon_scout", "celadon_scout", "identity.celadon_scout.v3", "content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png", "turnaround.outfit.celadon_scout_listening.v1"),
        new("character.cavern_ally", "cavern_ally", "identity.cavern_ally.v1", "content/visual-candidates/cx508/identity-anchors/identity_cavern_ally_v1.png", "turnaround.outfit.cavern_ally_stone_lamp.v1"),
        new("character.formation_spirit", "formation_spirit", "identity.formation_spirit.v2", "content/visual-candidates/cx509/identity-anchors/identity_formation_spirit_v2.png", "turnaround.outfit.formation_spirit_star_balance.v1"),
    ];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static IReadOnlyList<CatalogDiagnostic> Validate(
        string manifestPath,
        string sourceAdoptionPath,
        string assetRoot,
        string repositoryRoot)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-519 expression manifest JSON was empty.");
        var source = JsonNode.Parse(File.ReadAllText(sourceAdoptionPath))?.AsObject()
            ?? throw new InvalidDataException("CX-518 adoption JSON was empty.");

        ValidateSource(manifest, source, sourceAdoptionPath, diagnostics);
        ValidatePolicy(manifest, diagnostics);
        ValidateGlobalCoverage(manifest, diagnostics);

        var subjects = Objects(manifest, "subjects");
        var sourceAssets = Objects(source, "assets");
        for (var index = 0; index < Math.Min(subjects.Length, SubjectDefinitions.Length); index++)
        {
            ValidateSubject(
                subjects[index],
                SubjectDefinitions[index],
                sourceAssets,
                assetRoot,
                repositoryRoot,
                index,
                diagnostics);
        }

        ValidateMarkdown(manifest, subjects, repositoryRoot, diagnostics);
        return diagnostics;
    }

    private static void ValidateSource(
        JsonObject manifest,
        JsonObject source,
        string sourceAdoptionPath,
        List<CatalogDiagnostic> diagnostics)
    {
        var pin = manifest["sourceAdoption"]?.AsObject();
        var sourcePolicy = source["adoptionPolicy"]?.AsObject();
        var actualHash = Sha256(File.ReadAllBytes(sourceAdoptionPath));
        if (Value(manifest, "cardId") != "CX-519" ||
            Value(manifest, "worldId") != "lingmai-ember-red-mist" ||
            pin is null ||
            Value(pin, "cardId") != "CX-518" ||
            Value(pin, "path") != "content/story/red-mist/red-mist-multiview-adoption.cx518.json" ||
            !string.Equals(Value(pin, "sha256"), actualHash, StringComparison.Ordinal) ||
            pin["expressionGenerationAllowed"]?.GetValue<bool>() != true ||
            Value(source, "cardId") != "CX-518" ||
            sourcePolicy?["expressionGenerationAllowed"]?.GetValue<bool>() != true)
        {
            diagnostics.Add(new("source.adoption_hash", "$.sourceAdoption", "CX-519 must pin the exact approved CX-518 manifest and its expression-generation permission."));
        }
    }

    private static void ValidatePolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["generationPolicy"]?.AsObject();
        if (policy is null ||
            Value(policy, "imageProvider") != "codex_builtin_imagegen" ||
            policy["stillImagesOnly"]?.GetValue<bool>() != true ||
            Number(policy, "generationCalls") != 24 ||
            Number(policy, "successfulOutputs") != 19 ||
            Number(policy, "failedCalls") != 5 ||
            Number(policy, "discardedOutputs") != 1 ||
            Number(policy, "compositeBoardFiles") != 18 ||
            Number(policy, "independentExpressionFiles") != 72 ||
            Value(policy, "cropMethod") != "ffmpeg_crop_756x504_lanczos_to_1536x1024")
        {
            diagnostics.Add(new("policy.imagegen", "$.generationPolicy", "CX-519 must record the real built-in ImageGen and deterministic crop accounting."));
        }

        if (policy is null ||
            policy["videoOrAudioUsed"]?.GetValue<bool>() != false ||
            policy["flow2ApiUsed"]?.GetValue<bool>() != false ||
            policy["geminiUsed"]?.GetValue<bool>() != false ||
            policy["veoUsed"]?.GetValue<bool>() != false)
        {
            diagnostics.Add(new("policy.no_video_audio", "$.generationPolicy", "CX-519 may not call video or audio generation services."));
        }

        if (policy is null || Value(policy, "cx514Status") != "paused")
            diagnostics.Add(new("policy.cx514_paused", "$.generationPolicy.cx514Status", "CX-514 must remain paused."));
        if (policy is null || policy["overwriteExistingAssets"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.overwrite", "$.generationPolicy.overwriteExistingAssets", "CX-519 must not overwrite previously adopted assets."));
        if (policy is null || policy["runtimeUse"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.runtime", "$.generationPolicy.runtimeUse", "Expression references are not runtime assets."));
        if (policy is null || policy["shipInBuild"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.shipping", "$.generationPolicy.shipInBuild", "Expression references may not ship in the build."));
    }

    private static void ValidateGlobalCoverage(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        if (!Strings(manifest, "emotionOrder").SequenceEqual(RequiredEmotions, StringComparer.Ordinal))
            diagnostics.Add(new("coverage.emotions", "$.emotionOrder", "CX-519 must preserve the fixed ordered twelve-emotion set."));

        var subjects = Objects(manifest, "subjects");
        if (!subjects.Select(subject => Value(subject, "subjectId")).SequenceEqual(RequiredSubjects, StringComparer.Ordinal))
            diagnostics.Add(new("coverage.subjects", "$.subjects", "CX-519 must contain the fixed ordered six-character set."));

        var groups = Objects(manifest, "groupDefinitions");
        if (groups.Length != 3)
        {
            diagnostics.Add(new("coverage.groups", "$.groupDefinitions", "CX-519 requires groups A, B and C."));
            return;
        }

        for (var index = 0; index < groups.Length; index++)
        {
            if (Value(groups[index], "group") != RequiredGroups[index] ||
                !Strings(groups[index], "emotions").SequenceEqual(RequiredEmotions.Skip(index * 4).Take(4), StringComparer.Ordinal) ||
                !Strings(groups[index], "quadrants").SequenceEqual(RequiredQuadrants, StringComparer.Ordinal))
            {
                diagnostics.Add(new("coverage.groups", $"$.groupDefinitions[{index}]", "Every group must map four ordered emotions to top-left, top-right, bottom-left and bottom-right."));
            }
        }
    }

    private static void ValidateSubject(
        JsonObject subject,
        SubjectDefinition expected,
        IReadOnlyList<JsonObject> sourceAssets,
        string assetRoot,
        string repositoryRoot,
        int subjectIndex,
        List<CatalogDiagnostic> diagnostics)
    {
        var path = $"$.subjects[{subjectIndex}]";
        if (Value(subject, "subjectId") != expected.SubjectId || Value(subject, "slug") != expected.Slug)
            diagnostics.Add(new("coverage.subjects", path, "Subject ID and slug must match the fixed CX-519 order."));

        ValidateIdentityAnchor(subject["identityAnchor"]?.AsObject(), expected, repositoryRoot, path, diagnostics);
        ValidateOutfitReference(subject["outfitReference"]?.AsObject(), expected, sourceAssets, repositoryRoot, path, diagnostics);

        var boards = Objects(subject, "boards");
        if (boards.Length != 3)
            diagnostics.Add(new("coverage.groups", path + ".boards", "Every subject requires exactly three expression boards."));
        for (var groupIndex = 0; groupIndex < Math.Min(boards.Length, 3); groupIndex++)
            ValidateBoard(boards[groupIndex], expected, groupIndex, assetRoot, path, diagnostics);

        var expressions = Objects(subject, "expressions");
        if (!expressions.Select(expression => Value(expression, "emotionId")).SequenceEqual(RequiredEmotions, StringComparer.Ordinal))
            diagnostics.Add(new("coverage.emotions", path + ".expressions", "Every subject requires all twelve expressions in fixed order."));
        for (var emotionIndex = 0; emotionIndex < Math.Min(expressions.Length, RequiredEmotions.Length); emotionIndex++)
            ValidateExpression(expressions[emotionIndex], expected, emotionIndex, assetRoot, path, diagnostics);
    }

    private static void ValidateIdentityAnchor(
        JsonObject? anchor,
        SubjectDefinition expected,
        string repositoryRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var file = ResolveRepositoryFile(repositoryRoot, expected.IdentityPath);
        var actualHash = File.Exists(file) ? Sha256(File.ReadAllBytes(file)) : string.Empty;
        if (anchor is null ||
            Value(anchor, "assetId") != expected.IdentityAssetId ||
            Value(anchor, "path") != expected.IdentityPath ||
            !string.Equals(Value(anchor, "sha256"), actualHash, StringComparison.Ordinal))
        {
            diagnostics.Add(new("source.identity_anchor", path + ".identityAnchor", "Identity anchor path and SHA-256 must match the approved character anchor."));
        }
    }

    private static void ValidateOutfitReference(
        JsonObject? reference,
        SubjectDefinition expected,
        IReadOnlyList<JsonObject> sourceAssets,
        string repositoryRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var source = sourceAssets.SingleOrDefault(asset => Value(asset, "assetId") == expected.OutfitAssetId);
        var sourcePath = source is null ? string.Empty : Value(source, "sourcePath");
        var sourceHash = source?["artifact"] is JsonObject artifact ? Value(artifact, "sha256") : string.Empty;
        var actualFile = string.IsNullOrEmpty(sourcePath) ? string.Empty : ResolveRepositoryFile(repositoryRoot, sourcePath);
        var actualHash = File.Exists(actualFile) ? Sha256(File.ReadAllBytes(actualFile)) : string.Empty;
        if (reference is null ||
            Value(reference, "assetId") != expected.OutfitAssetId ||
            Value(reference, "path") != sourcePath ||
            !string.Equals(Value(reference, "sha256"), sourceHash, StringComparison.Ordinal) ||
            !string.Equals(sourceHash, actualHash, StringComparison.Ordinal))
        {
            diagnostics.Add(new("source.outfit_anchor", path + ".outfitReference", "Outfit reference must match the adopted CX-518 source path and SHA-256."));
        }
    }

    private static void ValidateBoard(
        JsonObject board,
        SubjectDefinition expected,
        int groupIndex,
        string assetRoot,
        string subjectPath,
        List<CatalogDiagnostic> diagnostics)
    {
        var group = RequiredGroups[groupIndex];
        var path = $"{subjectPath}.boards[{groupIndex}]";
        var expectedRelative = $"content/visual-candidates/cx519/expression-boards/expression_board_{expected.Slug}_group_{group}_v1.png";
        var expectedAssetId = $"expression_board.{expected.Slug}.group_{group}.v1";
        var expectedEmotions = RequiredEmotions.Skip(groupIndex * 4).Take(4).ToArray();
        if (Value(board, "assetId") != expectedAssetId ||
            Value(board, "group") != group ||
            !Strings(board, "emotions").SequenceEqual(expectedEmotions, StringComparer.Ordinal) ||
            Value(board, "outputPath") != expectedRelative)
        {
            diagnostics.Add(new("artifact.path", path, "Board ID, group, emotions and versioned output path must match the fixed mapping."));
        }

        var prompt = Value(board, "prompt");
        string[] requiredPromptTerms =
        [
            "exact fictional identity",
            "85mm",
            "straight-on",
            "high-key warm-ivory",
            "change facial muscles only",
            "no text",
            "no actor likeness",
            "identity drift",
            .. expectedEmotions,
        ];
        if (Value(board, "generationMode") != "codex_builtin_imagegen" ||
            requiredPromptTerms.Any(term => !prompt.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new("policy.imagegen", path + ".prompt", "Every board must record an identity-, camera-, light- and safety-locked built-in ImageGen prompt."));
        }

        ValidatePendingReview(board, path, diagnostics);
        ValidateArtifact(board, expectedRelative, assetRoot, path, diagnostics);
    }

    private static void ValidateExpression(
        JsonObject expression,
        SubjectDefinition expected,
        int emotionIndex,
        string assetRoot,
        string subjectPath,
        List<CatalogDiagnostic> diagnostics)
    {
        var emotion = RequiredEmotions[emotionIndex];
        var groupIndex = emotionIndex / 4;
        var quadrantIndex = emotionIndex % 4;
        var group = RequiredGroups[groupIndex];
        var quadrant = RequiredQuadrants[quadrantIndex];
        var path = $"{subjectPath}.expressions[{emotionIndex}]";
        var expectedRelative = $"content/visual-candidates/cx519/expressions/expression_{expected.Slug}_{emotion}_v1.png";
        var expectedBoard = $"content/visual-candidates/cx519/expression-boards/expression_board_{expected.Slug}_group_{group}_v1.png";
        if (Value(expression, "assetId") != $"expression.{expected.Slug}.{emotion}.v1" ||
            Value(expression, "emotionId") != emotion ||
            Value(expression, "outputPath") != expectedRelative ||
            Value(expression, "sourceBoardPath") != expectedBoard ||
            Value(expression, "sourceGroup") != group ||
            Value(expression, "sourceQuadrant") != quadrant ||
            Value(expression, "generationMode") != "deterministic_ffmpeg_crop")
        {
            diagnostics.Add(new("artifact.path", path, "Expression ID, source board, quadrant and versioned path must match the fixed mapping."));
        }

        var crop = expression["crop"]?.AsObject();
        var expectedX = quadrantIndex % 2 == 0 ? 6 : 774;
        var expectedY = quadrantIndex < 2 ? 4 : 516;
        if (crop is null ||
            Number(crop, "x") != expectedX ||
            Number(crop, "y") != expectedY ||
            Number(crop, "width") != 756 ||
            Number(crop, "height") != 504 ||
            Number(crop, "scaleWidth") != 1536 ||
            Number(crop, "scaleHeight") != 1024 ||
            Value(crop, "filter") != "lanczos")
        {
            diagnostics.Add(new("artifact.crop", path + ".crop", "Expression crop provenance must match the gutter-free deterministic Lanczos mapping."));
        }

        if (!Strings(expression, "reviewChecks").SequenceEqual(RequiredReviewChecks, StringComparer.Ordinal) ||
            Value(expression, "reviewStatus") != "pending_human_review")
        {
            diagnostics.Add(new("review.checks", path, "Every expression requires the fixed seven manual review checks."));
        }

        ValidatePendingReview(expression, path, diagnostics);
        ValidateArtifact(expression, expectedRelative, assetRoot, path, diagnostics);
    }

    private static void ValidatePendingReview(JsonObject asset, string path, List<CatalogDiagnostic> diagnostics)
    {
        if (Value(asset, "adoptionStatus") != "needs_review" || asset["adopted"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.pending_review", path, "CX-519 assets must remain unadopted and pending human review."));
        if (asset["runtimeUse"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.runtime", path, "CX-519 assets are not runtime content."));
        if (asset["shipInBuild"]?.GetValue<bool>() != false)
            diagnostics.Add(new("adoption.shipping", path, "CX-519 assets may not ship in the build."));
    }

    private static void ValidateArtifact(
        JsonObject asset,
        string expectedRelative,
        string assetRoot,
        string path,
        List<CatalogDiagnostic> diagnostics)
    {
        var relativeWithinRoot = expectedRelative["content/visual-candidates/cx519/".Length..]
            .Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(assetRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, relativeWithinRoot));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            diagnostics.Add(new("artifact.file_metadata", path + ".outputPath", "Expression asset is missing or outside the CX-519 root."));
            return;
        }

        var bytes = File.ReadAllBytes(resolved);
        var artifact = asset["artifact"]?.AsObject();
        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            artifact is null ||
            Value(artifact, "mediaType") != "image/png")
        {
            diagnostics.Add(new("artifact.png_signature", path + ".artifact", "Expression asset must be a valid PNG with IHDR and image/png media type."));
            return;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        if (Number(artifact, "byteLength") != bytes.LongLength ||
            Number(artifact, "width") != width ||
            Number(artifact, "height") != height ||
            width != 1536 || height != 1024)
        {
            diagnostics.Add(new("artifact.file_metadata", path + ".artifact", "PNG length and 1536x1024 dimensions must match the manifest."));
        }

        if (!string.Equals(Value(artifact, "sha256"), Sha256(bytes), StringComparison.Ordinal))
            diagnostics.Add(new("artifact.file_hash", path + ".artifact.sha256", "PNG SHA-256 must match the manifest."));
    }

    private static void ValidateMarkdown(
        JsonObject manifest,
        IReadOnlyList<JsonObject> subjects,
        string repositoryRoot,
        List<CatalogDiagnostic> diagnostics)
    {
        var contract = manifest["markdownContract"]?.AsObject();
        var relative = contract is null ? string.Empty : Value(contract, "relativePath");
        var resolved = ResolveRepositoryFile(repositoryRoot, relative);
        if (relative != "content/story/red-mist/red-mist-expression-references.cx519.md" ||
            contract?["listsEveryBoardAndExpression"]?.GetValue<bool>() != true ||
            !File.Exists(resolved))
        {
            diagnostics.Add(new("markdown.contract", "$.markdownContract", "CX-519 readable review record is missing."));
            return;
        }

        var markdown = File.ReadAllText(resolved);
        var allAssets = subjects.SelectMany(subject => Objects(subject, "boards").Concat(Objects(subject, "expressions")));
        if (allAssets.Any(asset =>
                !markdown.Contains(Value(asset, "assetId"), StringComparison.Ordinal) ||
                !markdown.Contains(Path.GetFileName(Value(asset, "outputPath")), StringComparison.Ordinal)))
        {
            diagnostics.Add(new("markdown.contract", "$.markdownContract", "Markdown must list all 18 boards and 72 expression files."));
        }
    }

    private static string ResolveRepositoryFile(string repositoryRoot, string relative)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? resolved
            : Path.Combine(root, "__invalid_path__");
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

    private sealed record SubjectDefinition(
        string SubjectId,
        string Slug,
        string IdentityAssetId,
        string IdentityPath,
        string OutfitAssetId);
}
