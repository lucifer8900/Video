using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using StoryValidator;

namespace NarrativeCompiler;

public sealed partial class ShotManifestGenerator
{
    private const int MaximumBundleBytes = 16 * 1024 * 1024;
    private const string OutputFileName = "shot-manifest.json";
    private const string SourceName = "story.bundle.json";
    private const string CostCurrency = "USD";

    private static readonly string[] AllowedDistributionStatuses =
    [
        "internal_prototype_only",
        "release_candidate",
        "shipping",
    ];

    private static readonly string[] InvalidInputKinds =
    [
        "abuse",
        "irrelevant",
        "too_long",
        "silence",
        "low_confidence",
    ];

    public string Generate(string bundlePath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fullBundlePath = Path.GetFullPath(bundlePath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var destinationPath = Path.GetFullPath(
            Path.Combine(fullOutputDirectory, OutputFileName));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullBundlePath, destinationPath, pathComparison))
        {
            throw Error(
                "/",
                "input and output resolve to the same file; refusing to overwrite the source bundle");
        }

        var bundleBytes = ReadBounded(fullBundlePath);
        RejectDuplicateProperties(bundleBytes);
        ValidateSourceBundle(fullBundlePath);
        var bundle = ParseObject(bundleBytes);

        var schemaVersion = RequiredString(bundle, "schemaVersion", "/schemaVersion");
        if (!string.Equals(schemaVersion, "1.0.0", StringComparison.Ordinal))
        {
            throw Error("/schemaVersion", "expected the value '1.0.0'");
        }

        RequireValue(bundle, "approvalStatus", "approved", "/approvalStatus");
        RequireAllowedValue(
            bundle,
            "distributionStatus",
            AllowedDistributionStatuses,
            "/distributionStatus");

        var bundleId = RequiredString(bundle, "id", "/id");
        var bundleContentHash = RequiredSha256(bundle, "contentHash", "/contentHash");
        var computedBundleHash = ComputeContentHash(bundle);
        if (!string.Equals(bundleContentHash, computedBundleHash, StringComparison.Ordinal))
        {
            throw Error(
                "/contentHash",
                $"does not match canonical bundle content (expected '{computedBundleHash}')");
        }

        var localizationRoots = RequiredObject(bundle, "localizations", "/localizations");
        var (language, localization) = SelectLocalization(localizationRoots);

        var mediaAssets = RequiredArray(bundle, "mediaAssets", "/mediaAssets");
        var mediaIndex = BuildMediaIndex(mediaAssets);
        ValidateMediaFallbackChains(mediaIndex);

        var voiceIntents = RequiredArray(bundle, "voiceIntents", "/voiceIntents");
        var voiceIntentIndex = BuildApprovedEntityIndex(voiceIntents, "/voiceIntents");
        var npcResponses = RequiredArray(bundle, "npcResponses", "/npcResponses");
        var npcResponseIndex = BuildApprovedEntityIndex(npcResponses, "/npcResponses");
        var sceneNodes = RequiredArray(bundle, "sceneNodes", "/sceneNodes");
        if (sceneNodes.Count == 0)
        {
            throw Error("/sceneNodes", "expected at least one scene node");
        }

        var responseSourceNodes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var shots = BuildShots(
            sceneNodes,
            mediaIndex,
            localization,
            bundleId,
            bundleContentHash,
            voiceIntentIndex,
            npcResponseIndex,
            responseSourceNodes);
        var responseRequirements = BuildResponseRequirements(
            npcResponses,
            responseSourceNodes,
            mediaIndex,
            localization);

        var manifest = new JsonObject
        {
            ["schemaVersion"] = "1.0.0",
            ["manifestId"] = $"shot-manifest.{bundleId}",
            ["sourceBundleId"] = bundleId,
            ["sourceBundleContentHash"] = bundleContentHash,
            ["language"] = language,
            ["currency"] = CostCurrency,
            ["approvalStatus"] = "needs_review",
            ["shotDispatchAllowed"] = false,
            ["responseRequirements"] = responseRequirements,
            ["shots"] = shots,
            ["contentHash"] = string.Empty,
        };

        manifest["contentHash"] = ComputeContentHash(manifest);
        ValidateGeneratedManifest(manifest);
        return WriteAtomically(destinationPath, SerializeIndented(manifest));
    }

    private static JsonArray BuildShots(
        JsonArray sceneNodes,
        IReadOnlyDictionary<string, MediaAssetRecord> mediaIndex,
        JsonObject localization,
        string bundleId,
        string bundleContentHash,
        IReadOnlyDictionary<string, EntityRecord> voiceIntentIndex,
        IReadOnlyDictionary<string, EntityRecord> npcResponseIndex,
        IDictionary<string, List<string>> responseSourceNodes)
    {
        var shots = new JsonArray();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < sceneNodes.Count; index++)
        {
            var pointer = $"/sceneNodes/{index}";
            if (sceneNodes[index] is not JsonObject node)
            {
                throw Error(pointer, "expected a JSON object");
            }

            RequireValue(node, "approvalStatus", "approved", $"{pointer}/approvalStatus");
            var nodeId = RequiredString(node, "id", $"{pointer}/id");
            if (!nodeIds.Add(nodeId))
            {
                throw Error($"{pointer}/id", $"duplicate node id '{nodeId}'");
            }

            var nodeKind = RequiredString(node, "nodeKind", $"{pointer}/nodeKind");
            var backgroundRef = RequiredString(
                node,
                "backgroundMediaRef",
                $"{pointer}/backgroundMediaRef");
            var background = RequiredMedia(
                mediaIndex,
                backgroundRef,
                $"{pointer}/backgroundMediaRef");
            RequireMediaType(
                background,
                ["image"],
                $"{pointer}/backgroundMediaRef",
                "must reference an approved image");

            var portraitRef = OptionalString(
                node,
                "portraitMediaRef",
                $"{pointer}/portraitMediaRef");
            if (portraitRef is not null)
            {
                RequireMediaType(
                    RequiredMedia(mediaIndex, portraitRef, $"{pointer}/portraitMediaRef"),
                    ["image"],
                    $"{pointer}/portraitMediaRef",
                    "must reference an approved image");
            }

            var introRef = OptionalString(
                node,
                "introMediaRef",
                $"{pointer}/introMediaRef");
            MediaAssetRecord? primaryMedia = null;
            if (introRef is not null)
            {
                primaryMedia = RequiredMedia(
                    mediaIndex,
                    introRef,
                    $"{pointer}/introMediaRef");
                RequireMediaType(
                    primaryMedia,
                    ["video", "animation"],
                    $"{pointer}/introMediaRef",
                    "must reference approved video or animation media");
            }

            var explicitFallbackRef = OptionalString(
                node,
                "fallbackMediaRef",
                $"{pointer}/fallbackMediaRef");
            if (explicitFallbackRef is not null)
            {
                RequiredMedia(
                    mediaIndex,
                    explicitFallbackRef,
                    $"{pointer}/fallbackMediaRef");
            }

            var namedMedia = new HashSet<string>(StringComparer.Ordinal)
            {
                backgroundRef,
            };
            if (portraitRef is not null) namedMedia.Add(portraitRef);
            if (introRef is not null) namedMedia.Add(introRef);
            if (explicitFallbackRef is not null) namedMedia.Add(explicitFallbackRef);
            var auxiliaryRefs = new JsonArray();
            var mediaRefs = RequiredArray(node, "mediaRefs", $"{pointer}/mediaRefs");
            for (var mediaIndexInNode = 0; mediaIndexInNode < mediaRefs.Count; mediaIndexInNode++)
            {
                var mediaRefPointer = $"{pointer}/mediaRefs/{mediaIndexInNode}";
                var mediaRef = RequiredStringValue(mediaRefs[mediaIndexInNode], mediaRefPointer);
                RequiredMedia(mediaIndex, mediaRef, mediaRefPointer);
                if (!namedMedia.Contains(mediaRef))
                {
                    auxiliaryRefs.Add(mediaRef);
                }
            }

            var mediaAssetFallbackRef = primaryMedia?.FallbackMediaRef;
            var fallbackRef = explicitFallbackRef ?? mediaAssetFallbackRef ?? backgroundRef;
            var fallbackOrigin = explicitFallbackRef is not null
                ? "explicit"
                : mediaAssetFallbackRef is not null
                    ? "media_asset_fallback"
                    : "background_placeholder";
            var fallbackPointer = explicitFallbackRef is not null
                ? $"{pointer}/fallbackMediaRef"
                : mediaAssetFallbackRef is not null && primaryMedia is not null
                    ? $"/mediaAssets/{primaryMedia.Index}/fallbackMediaRef"
                    : $"{pointer}/backgroundMediaRef";
            var fallbackMedia = RequiredMedia(mediaIndex, fallbackRef, fallbackPointer);
            RequireMediaType(
                fallbackMedia,
                ["image", "video", "animation"],
                fallbackPointer,
                "must reference playable visual fallback media");
            var maleKey = RequiredString(node, "maleTextKey", $"{pointer}/maleTextKey");
            var femaleKey = RequiredString(node, "femaleTextKey", $"{pointer}/femaleTextKey");
            var dialogueVariants = new JsonArray
            {
                Dialogue(
                    "male",
                    maleKey,
                    RequiredLocalizedText(localization, maleKey, $"{pointer}/maleTextKey")),
                Dialogue(
                    "female",
                    femaleKey,
                    RequiredLocalizedText(localization, femaleKey, $"{pointer}/femaleTextKey")),
            };

            var nodeResponseRefs = ResolveNodeResponseRefs(
                node,
                pointer,
                voiceIntentIndex,
                npcResponseIndex);
            foreach (var responseRef in nodeResponseRefs)
            {
                if (!responseSourceNodes.TryGetValue(responseRef, out var sourceNodes))
                {
                    sourceNodes = [];
                    responseSourceNodes[responseRef] = sourceNodes;
                }

                if (!sourceNodes.Contains(nodeId, StringComparer.Ordinal))
                {
                    sourceNodes.Add(nodeId);
                }
            }

            var frame = FrameReference(background);
            var issues = new List<string>
            {
                "character_state.missing",
                "frame.background_placeholder",
                "human_review.required",
                "source.original_missing",
            };
            long? durationMilliseconds = primaryMedia?.DurationMilliseconds is > 0
                ? primaryMedia.DurationMilliseconds
                : null;
            if (durationMilliseconds is null)
            {
                issues.Add("duration.missing");
            }

            if (introRef is null)
            {
                issues.Add("generation.g2_blocked");
                issues.Add("primary_video.missing");
            }

            issues.Sort(StringComparer.Ordinal);
            shots.Add(new JsonObject
            {
                ["shotId"] = $"shot.{nodeId}.primary",
                ["nodeId"] = nodeId,
                ["nodeKind"] = nodeKind,
                ["source"] = new JsonObject
                {
                    ["sourceId"] = bundleId,
                    ["locator"] = pointer,
                    ["evidenceHash"] = bundleContentHash,
                    ["needsReview"] = true,
                },
                ["dialogueVariants"] = dialogueVariants,
                ["dialogueBinding"] = "unbound_node_text",
                ["mediaRoles"] = new JsonObject
                {
                    ["backgroundMediaRef"] = backgroundRef,
                    ["portraitMediaRef"] = NodeOrNull(portraitRef),
                    ["introMediaRef"] = NodeOrNull(introRef),
                    ["explicitFallbackMediaRef"] = NodeOrNull(explicitFallbackRef),
                    ["auxiliaryMediaRefs"] = auxiliaryRefs,
                },
                ["npcResponseRefs"] = StringArray(nodeResponseRefs),
                ["targetDurationMilliseconds"] = durationMilliseconds is null
                    ? null
                    : JsonValue.Create(durationMilliseconds.Value),
                ["firstFrame"] = frame.DeepClone(),
                ["lastFrame"] = frame,
                ["primaryMedia"] = primaryMedia is null ? null : MediaPin(primaryMedia),
                ["generationTier"] = introRef is null
                    ? "blocked_pending_g2"
                    : "reuse_only",
                ["maximumCostMicros"] = 0,
                ["fallbackMedia"] = MediaPin(fallbackMedia),
                ["fallbackOrigin"] = fallbackOrigin,
                ["characterStateRefs"] = new JsonArray(),
                ["readinessStatus"] = introRef is null ? "blocked" : "needs_review",
                ["issueCodes"] = StringArray(issues),
                ["dispatchAllowed"] = false,
            });
        }

        return shots;
    }

    private static JsonArray BuildResponseRequirements(
        JsonArray npcResponses,
        IReadOnlyDictionary<string, List<string>> responseSourceNodes,
        IReadOnlyDictionary<string, MediaAssetRecord> mediaIndex,
        JsonObject localization)
    {
        var requirements = new JsonArray();
        for (var index = 0; index < npcResponses.Count; index++)
        {
            var pointer = $"/npcResponses/{index}";
            if (npcResponses[index] is not JsonObject response)
            {
                throw Error(pointer, "expected a JSON object");
            }

            var responseId = RequiredString(response, "id", $"{pointer}/id");
            if (!responseSourceNodes.TryGetValue(responseId, out var sourceNodeIds))
            {
                continue;
            }

            RequireValue(response, "approvalStatus", "approved", $"{pointer}/approvalStatus");
            var textKey = RequiredString(response, "textKey", $"{pointer}/textKey");
            var emotionKey = RequiredString(response, "emotionKey", $"{pointer}/emotionKey");
            var lipSyncRef = RequiredString(
                response,
                "lipSyncMediaRef",
                $"{pointer}/lipSyncMediaRef");
            var lipSyncMedia = RequiredMedia(
                mediaIndex,
                lipSyncRef,
                $"{pointer}/lipSyncMediaRef");
            var audioRef = OptionalString(
                response,
                "audioMediaRef",
                $"{pointer}/audioMediaRef");
            MediaAssetRecord? audioMedia = audioRef is null
                ? null
                : RequiredMedia(mediaIndex, audioRef, $"{pointer}/audioMediaRef");
            var explicitFallbackRef = OptionalString(
                response,
                "fallbackMediaRef",
                $"{pointer}/fallbackMediaRef");
            var fallbackRef = explicitFallbackRef ?? lipSyncMedia.FallbackMediaRef;
            var fallbackMedia = fallbackRef is null
                ? null
                : RequiredMedia(
                    mediaIndex,
                    fallbackRef,
                    explicitFallbackRef is not null
                        ? $"{pointer}/fallbackMediaRef"
                        : $"/mediaAssets/{lipSyncMedia.Index}/fallbackMediaRef");

            var issues = new List<string>
            {
                "human_review.required",
                "source.original_missing",
            };
            var blocked = false;
            if (lipSyncMedia.MediaType is not ("video" or "animation"))
            {
                issues.Add("lip_sync.placeholder_media");
                blocked = true;
            }

            if (audioMedia is null)
            {
                issues.Add("audio.missing");
                blocked = true;
            }
            else if (!string.Equals(audioMedia.MediaType, "audio", StringComparison.Ordinal))
            {
                issues.Add("audio.invalid_media_type");
                blocked = true;
            }

            if (fallbackRef is null)
            {
                issues.Add("fallback.missing");
                blocked = true;
            }
            else if (fallbackMedia!.MediaType is not ("video" or "animation" or "image"))
            {
                issues.Add("fallback.invalid_media_type");
                blocked = true;
            }

            issues.Sort(StringComparer.Ordinal);
            requirements.Add(new JsonObject
            {
                ["responseId"] = responseId,
                ["sourceNodeIds"] = StringArray(sourceNodeIds),
                ["text"] = LocalizedValue(
                    textKey,
                    RequiredLocalizedText(localization, textKey, $"{pointer}/textKey")),
                ["emotion"] = LocalizedValue(
                    emotionKey,
                    RequiredLocalizedText(localization, emotionKey, $"{pointer}/emotionKey")),
                ["lipSyncMedia"] = MediaPin(lipSyncMedia),
                ["audioMedia"] = audioMedia is null ? null : MediaPin(audioMedia),
                ["fallbackMedia"] = fallbackMedia is null ? null : MediaPin(fallbackMedia),
                ["readinessStatus"] = blocked ? "blocked" : "needs_review",
                ["issueCodes"] = StringArray(issues),
            });
        }

        return requirements;
    }

    private static IReadOnlyList<string> ResolveNodeResponseRefs(
        JsonObject node,
        string pointer,
        IReadOnlyDictionary<string, EntityRecord> voiceIntentIndex,
        IReadOnlyDictionary<string, EntityRecord> npcResponseIndex)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allowlist = new HashSet<string>(StringComparer.Ordinal);

        void AddResponse(string responseRef, string referencePointer)
        {
            if (!npcResponseIndex.ContainsKey(responseRef))
            {
                throw Error(referencePointer, $"references unknown NPC response '{responseRef}'");
            }

            if (seen.Add(responseRef))
            {
                result.Add(responseRef);
            }
        }

        void AddAllowlistedResponse(string responseRef, string referencePointer)
        {
            if (!allowlist.Contains(responseRef))
            {
                throw Error(
                    referencePointer,
                    $"NPC response '{responseRef}' is outside the node npcResponseRefs allowlist");
            }

            AddResponse(responseRef, referencePointer);
        }

        var directRefs = OptionalArray(node, "npcResponseRefs", $"{pointer}/npcResponseRefs");
        for (var index = 0; index < directRefs.Count; index++)
        {
            var refPointer = $"{pointer}/npcResponseRefs/{index}";
            var responseRef = RequiredStringValue(directRefs[index], refPointer);
            AddResponse(responseRef, refPointer);
            allowlist.Add(responseRef);
        }

        var intentRefs = RequiredArray(node, "voiceIntentRefs", $"{pointer}/voiceIntentRefs");
        for (var index = 0; index < intentRefs.Count; index++)
        {
            var intentPointer = $"{pointer}/voiceIntentRefs/{index}";
            var intentRef = RequiredStringValue(intentRefs[index], intentPointer);
            if (!voiceIntentIndex.TryGetValue(intentRef, out var intent))
            {
                throw Error(intentPointer, $"references unknown voice intent '{intentRef}'");
            }

            var primaryRef = RequiredString(
                intent.Json,
                "npcResponseRef",
                $"/voiceIntents/{intent.Index}/npcResponseRef");
            AddAllowlistedResponse(
                primaryRef,
                $"/voiceIntents/{intent.Index}/npcResponseRef");
            var fallbackRef = OptionalString(
                intent.Json,
                "fallbackNpcResponseRef",
                $"/voiceIntents/{intent.Index}/fallbackNpcResponseRef");
            if (fallbackRef is not null)
            {
                AddAllowlistedResponse(
                    fallbackRef,
                    $"/voiceIntents/{intent.Index}/fallbackNpcResponseRef");
            }
        }

        if (node["invalid_input_rules"] is JsonObject invalidRules)
        {
            foreach (var kind in InvalidInputKinds)
            {
                if (invalidRules[kind] is not JsonObject rule)
                {
                    throw Error(
                        $"{pointer}/invalid_input_rules/{kind}",
                        "expected a JSON object");
                }

                var refPointer =
                    $"{pointer}/invalid_input_rules/{kind}/npcResponseRef";
                AddAllowlistedResponse(
                    RequiredString(rule, "npcResponseRef", refPointer),
                    refPointer);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, EntityRecord> BuildApprovedEntityIndex(
        JsonArray entities,
        string arrayPointer)
    {
        var result = new Dictionary<string, EntityRecord>(StringComparer.Ordinal);
        for (var index = 0; index < entities.Count; index++)
        {
            var pointer = $"{arrayPointer}/{index}";
            if (entities[index] is not JsonObject entity)
            {
                throw Error(pointer, "expected a JSON object");
            }

            RequireValue(entity, "approvalStatus", "approved", $"{pointer}/approvalStatus");
            var id = RequiredString(entity, "id", $"{pointer}/id");
            if (!result.TryAdd(id, new EntityRecord(entity, index)))
            {
                throw Error($"{pointer}/id", $"duplicate entity id '{id}'");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, MediaAssetRecord> BuildMediaIndex(
        JsonArray mediaAssets)
    {
        var result = new Dictionary<string, MediaAssetRecord>(StringComparer.Ordinal);
        for (var index = 0; index < mediaAssets.Count; index++)
        {
            var pointer = $"/mediaAssets/{index}";
            if (mediaAssets[index] is not JsonObject asset)
            {
                throw Error(pointer, "expected a JSON object");
            }

            RequireValue(asset, "approvalStatus", "approved", $"{pointer}/approvalStatus");
            var id = RequiredString(asset, "id", $"{pointer}/id");
            var mediaType = RequiredString(asset, "mediaType", $"{pointer}/mediaType");
            var assetVersion = RequiredString(
                asset,
                "assetVersion",
                $"{pointer}/assetVersion");
            var contentHash = RequiredSha256(
                asset,
                "contentHash",
                $"{pointer}/contentHash");
            var duration = OptionalNonnegativeInt64(
                asset,
                "durationMilliseconds",
                $"{pointer}/durationMilliseconds");
            var fallbackRef = OptionalString(
                asset,
                "fallbackMediaRef",
                $"{pointer}/fallbackMediaRef");
            if (!result.TryAdd(
                    id,
                    new MediaAssetRecord(
                        index,
                        id,
                        mediaType,
                        assetVersion,
                        contentHash,
                        duration,
                        fallbackRef)))
            {
                throw Error($"{pointer}/id", $"duplicate media id '{id}'");
            }
        }

        return result;
    }

    private static void ValidateMediaFallbackChains(
        IReadOnlyDictionary<string, MediaAssetRecord> mediaIndex)
    {
        foreach (var origin in mediaIndex.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal)
            {
                origin.Id,
            };
            var current = origin;
            while (current.FallbackMediaRef is { } fallbackRef)
            {
                if (!mediaIndex.TryGetValue(fallbackRef, out var fallback))
                {
                    throw Error(
                        $"/mediaAssets/{current.Index}/fallbackMediaRef",
                        $"references unknown media '{fallbackRef}'");
                }

                if (!visited.Add(fallbackRef))
                {
                    throw Error(
                        $"/mediaAssets/{current.Index}/fallbackMediaRef",
                        $"media fallback cycle reaches '{fallbackRef}'");
                }

                current = fallback;
            }
        }
    }

    private static (string Language, JsonObject Localization) SelectLocalization(
        JsonObject localizationRoots)
    {
        if (localizationRoots.TryGetPropertyValue("zh-CN", out var chineseNode))
        {
            return (
                "zh-CN",
                chineseNode as JsonObject
                ?? throw Error("/localizations/zh-CN", "expected a JSON object"));
        }

        if (localizationRoots.Count != 1)
        {
            throw Error(
                "/localizations",
                "expected zh-CN or exactly one language for deterministic output");
        }

        var property = localizationRoots.Single();
        return (
            property.Key,
            property.Value as JsonObject
            ?? throw Error(
                $"/localizations/{EscapePointer(property.Key)}",
                "expected a JSON object"));
    }

    private static void ValidateSourceBundle(string bundlePath)
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas");
        if (!Directory.Exists(schemaDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The contract schema directory was not found: {schemaDirectory}");
        }

        var validation = new StoryBundleValidator().Validate(bundlePath, schemaDirectory);
        if (validation.Valid)
        {
            return;
        }

        var first = validation.Diagnostics[0];
        throw Error(first.Path, $"{first.Code}: {first.Message}");
    }

    private static void ValidateGeneratedManifest(JsonObject manifest)
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas");
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        var schemas = new List<JsonSchema>();
        JsonSchema? manifestSchema = null;
        foreach (var schemaPath in Directory
                     .EnumerateFiles(schemaDirectory, "*.schema.json")
                     .Order(StringComparer.Ordinal))
        {
            using var schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var schema = JsonSchema.Build(schemaDocument.RootElement.Clone(), buildOptions);
            schemas.Add(schema);
            if (string.Equals(
                    Path.GetFileName(schemaPath),
                    "shot-manifest.schema.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                manifestSchema = schema;
            }
        }

        if (manifestSchema is null)
        {
            throw new FileNotFoundException(
                "The shot manifest schema was not found.",
                Path.Combine(schemaDirectory, "shot-manifest.schema.json"));
        }

        using var instance = JsonDocument.Parse(manifest.ToJsonString());
        var evaluation = manifestSchema.Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
        if (!evaluation.IsValid)
        {
            throw new InvalidDataException(
                "shot-manifest.schema.json: generated manifest does not satisfy the contract.");
        }
    }

    private static JsonObject Dialogue(string route, string key, string text) => new()
    {
        ["route"] = route,
        ["localizationKey"] = key,
        ["text"] = text,
    };

    private static JsonObject LocalizedValue(string key, string text) => new()
    {
        ["localizationKey"] = key,
        ["text"] = text,
    };

    private static JsonObject FrameReference(MediaAssetRecord media) => new()
    {
        ["mediaRef"] = media.Id,
        ["assetVersion"] = media.AssetVersion,
        ["contentHash"] = media.ContentHash,
        ["mediaType"] = "image",
        ["origin"] = "background_placeholder",
    };

    private static JsonObject MediaPin(MediaAssetRecord media) => new()
    {
        ["mediaRef"] = media.Id,
        ["assetVersion"] = media.AssetVersion,
        ["contentHash"] = media.ContentHash,
        ["mediaType"] = media.MediaType,
    };

    private static JsonNode? NodeOrNull(string? value) =>
        value is null ? null : JsonValue.Create(value);

    private static JsonArray StringArray(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static MediaAssetRecord RequiredMedia(
        IReadOnlyDictionary<string, MediaAssetRecord> mediaIndex,
        string reference,
        string pointer) =>
        mediaIndex.TryGetValue(reference, out var media)
            ? media
            : throw Error(pointer, $"references unknown media '{reference}'");

    private static void RequireMediaType(
        MediaAssetRecord media,
        IReadOnlyCollection<string> allowedTypes,
        string pointer,
        string message)
    {
        if (!allowedTypes.Contains(media.MediaType, StringComparer.Ordinal))
        {
            throw Error(pointer, message);
        }
    }

    private static string RequiredLocalizedText(
        JsonObject localization,
        string key,
        string pointer)
    {
        if (localization[key] is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw Error(pointer, $"localization key '{key}' is missing or empty");
    }

    private static byte[] ReadBounded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"{SourceName} was not found.", path);
        }

        if (info.Length > MaximumBundleBytes)
        {
            throw Error("/", $"input exceeds {MaximumBundleBytes} bytes");
        }

        return File.ReadAllBytes(path);
    }

    private static JsonObject ParseObject(byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(
                       bytes,
                       nodeOptions: null,
                       documentOptions: new JsonDocumentOptions
                       {
                           AllowTrailingCommas = false,
                           CommentHandling = JsonCommentHandling.Disallow,
                           MaxDepth = 64,
                       }) as JsonObject
                   ?? throw Error("/", "expected a JSON object");
        }
        catch (JsonException exception)
        {
            throw Error("/", $"invalid JSON: {exception.Message}");
        }
    }

    private static void RejectDuplicateProperties(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            VisitForDuplicates(document.RootElement, string.Empty);
        }
        catch (JsonException exception)
        {
            throw Error("/", $"invalid JSON: {exception.Message}");
        }
    }

    private static void VisitForDuplicates(JsonElement element, string pointer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                var childPointer = $"{pointer}/{EscapePointer(property.Name)}";
                if (!names.Add(property.Name))
                {
                    throw Error(childPointer, $"duplicate property '{property.Name}'");
                }

                VisitForDuplicates(property.Value, childPointer);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                VisitForDuplicates(item, $"{pointer}/{index++}");
            }
        }
    }

    private static void RequireValue(
        JsonObject owner,
        string propertyName,
        string expected,
        string pointer)
    {
        var actual = RequiredString(owner, propertyName, pointer);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Error(pointer, $"expected the value '{expected}'");
        }
    }

    private static string RequireAllowedValue(
        JsonObject owner,
        string propertyName,
        IReadOnlyCollection<string> allowed,
        string pointer)
    {
        var actual = RequiredString(owner, propertyName, pointer);
        return allowed.Contains(actual, StringComparer.Ordinal)
            ? actual
            : throw Error(pointer, $"value '{actual}' is not allowed");
    }

    private static string RequiredString(
        JsonObject owner,
        string propertyName,
        string pointer) =>
        RequiredStringValue(owner[propertyName], pointer);

    private static string RequiredStringValue(JsonNode? node, string pointer)
    {
        if (node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw Error(pointer, "expected a non-empty string");
    }

    private static string? OptionalString(
        JsonObject owner,
        string propertyName,
        string pointer)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        return RequiredStringValue(owner[propertyName], pointer);
    }

    private static string RequiredSha256(
        JsonObject owner,
        string propertyName,
        string pointer)
    {
        var value = RequiredString(owner, propertyName, pointer);
        return Sha256Pattern().IsMatch(value)
            ? value
            : throw Error(pointer, "expected a lowercase sha256 value");
    }

    private static long? OptionalNonnegativeInt64(
        JsonObject owner,
        string propertyName,
        string pointer)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        if (owner[propertyName] is JsonValue value &&
            value.TryGetValue<long>(out var number) &&
            number >= 0)
        {
            return number;
        }

        throw Error(pointer, "expected a nonnegative integer");
    }

    private static JsonObject RequiredObject(
        JsonObject owner,
        string propertyName,
        string pointer) =>
        owner[propertyName] as JsonObject
        ?? throw Error(pointer, "expected a JSON object");

    private static JsonArray RequiredArray(
        JsonObject owner,
        string propertyName,
        string pointer) =>
        owner[propertyName] as JsonArray
        ?? throw Error(pointer, "expected an array");

    private static JsonArray OptionalArray(
        JsonObject owner,
        string propertyName,
        string pointer)
    {
        if (owner[propertyName] is null)
        {
            return new JsonArray();
        }

        return owner[propertyName] as JsonArray
            ?? throw Error(pointer, "expected an array");
    }

    private static string ComputeContentHash(JsonObject value)
    {
        var content = value.DeepClone().AsObject();
        content.Remove("contentHash");
        return "sha256:" +
            Convert.ToHexString(SHA256.HashData(SerializeCanonical(content)))
                .ToLowerInvariant();
    }

    private static byte[] SerializeCanonical(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, node);
        }

        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(
                             property => property.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value)
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static byte[] SerializeIndented(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            node.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static string WriteAtomically(string destinationPath, byte[] bytes)
    {
        var outputDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new DirectoryNotFoundException(
                "Could not resolve the shot manifest output directory.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{OutputFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static InvalidDataException Error(string pointer, string message) =>
        new($"{SourceName}#{(string.IsNullOrEmpty(pointer) ? "/" : pointer)}: {message}.");

    [GeneratedRegex("^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed record MediaAssetRecord(
        int Index,
        string Id,
        string MediaType,
        string AssetVersion,
        string ContentHash,
        long? DurationMilliseconds,
        string? FallbackMediaRef);

    private sealed record EntityRecord(JsonObject Json, int Index);
}
