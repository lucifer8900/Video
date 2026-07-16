using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist
{
    public enum StoryBundleErrorCode
    {
        None,
        IoFailure,
        JsonMalformed,
        UnsupportedVersion,
        SchemaInvalid,
        HashMismatch
    }

    public sealed class StoryBundleIdentity
    {
        public StoryBundleIdentity(string bundleId, string version, string contentHash)
        {
            BundleId = bundleId ?? string.Empty;
            Version = version ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
        }

        public string BundleId { get; }
        public string Version { get; }
        public string ContentHash { get; }
    }

    public sealed class StoryMediaAsset
    {
        public StoryMediaAsset(string id, string mediaType, string uri, string contentHash)
        {
            Id = id;
            MediaType = mediaType;
            Uri = uri;
            ContentHash = contentHash;
        }

        public string Id { get; }
        public string MediaType { get; }
        public string Uri { get; }
        public string ContentHash { get; }
    }

    public sealed class StoryBundle
    {
        public StoryBundle(
            StoryBundleIdentity identity,
            string entryNodeId,
            IReadOnlyDictionary<string, StoryNode> nodes,
            IReadOnlyDictionary<string, StoryVoiceIntent> voiceIntents,
            IReadOnlyDictionary<string, StoryNpcResponse> npcResponses,
            IReadOnlyDictionary<string, StoryMediaAsset> mediaAssets)
        {
            Identity = identity;
            EntryNodeId = entryNodeId;
            Nodes = nodes;
            VoiceIntents = voiceIntents;
            NpcResponses = npcResponses;
            MediaAssets = mediaAssets;
        }

        public StoryBundleIdentity Identity { get; }
        public string EntryNodeId { get; }
        public IReadOnlyDictionary<string, StoryNode> Nodes { get; }
        public IReadOnlyDictionary<string, StoryVoiceIntent> VoiceIntents { get; }
        public IReadOnlyDictionary<string, StoryNpcResponse> NpcResponses { get; }
        public IReadOnlyDictionary<string, StoryMediaAsset> MediaAssets { get; }
    }

    public sealed class StoryBundleLoadResult
    {
        private StoryBundleLoadResult(
            StoryBundle bundle,
            StoryBundleErrorCode errorCode,
            string userMessage,
            string technicalMessage)
        {
            Bundle = bundle;
            ErrorCode = errorCode;
            UserMessage = userMessage ?? string.Empty;
            TechnicalMessage = technicalMessage ?? string.Empty;
        }

        public bool Success => Bundle != null && ErrorCode == StoryBundleErrorCode.None;
        public StoryBundle Bundle { get; }
        public StoryBundleErrorCode ErrorCode { get; }
        public string UserMessage { get; }
        public string TechnicalMessage { get; }

        public static StoryBundleLoadResult Succeeded(StoryBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            return new StoryBundleLoadResult(bundle, StoryBundleErrorCode.None, string.Empty, string.Empty);
        }

        public static StoryBundleLoadResult Failed(
            StoryBundleErrorCode errorCode,
            string userMessage,
            string technicalMessage)
        {
            if (errorCode == StoryBundleErrorCode.None)
                throw new ArgumentException("A failed result requires an error code.", nameof(errorCode));
            return new StoryBundleLoadResult(null, errorCode, userMessage, technicalMessage);
        }
    }

    public sealed class StoryBundleErrorPageModel
    {
        private StoryBundleErrorPageModel(string title, string message, bool canContinue)
        {
            Title = title;
            Message = message;
            CanContinue = canContinue;
        }

        public string Title { get; }
        public string Message { get; }
        public bool CanContinue { get; }

        public static StoryBundleErrorPageModel From(StoryBundleLoadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            string message = result.UserMessage;
            if (!string.IsNullOrWhiteSpace(result.TechnicalMessage))
            {
                message += "\n\n诊断：" + result.TechnicalMessage;
            }
            message += "\n\n请校验游戏文件或重新安装当前版本。";
            return new StoryBundleErrorPageModel("剧情包无法加载", message, false);
        }
    }

    public static class StoryBundleLoader
    {
        public const string BundleFileName = "story.bundle.json";
        public const string SupportedSchemaVersion = "1.0.0";

        private static readonly string[] RootProperties =
        {
            "schemaVersion", "id", "entryNodeId", "contentHash", "approvalStatus",
            "distributionStatus", "localizations", "storyFacts", "characterDefinitions",
            "characterStates", "questDefinitions", "sceneNodes", "voiceIntents", "npcResponses",
            "combatantStates", "encounterDefinitions", "combatResolutions", "mediaAssets",
            "generationJobs", "saveGames"
        };

        private static readonly string[] NodeProperties =
        {
            "schemaVersion", "id", "displayKey", "titleKey", "locationKey", "speakerKey",
            "maleTextKey", "femaleTextKey", "nodeKind", "estimatedMinutes", "entryConditions",
            "mediaRefs", "fallbackMediaRef", "backgroundMediaRef", "portraitMediaRef", "introMediaRef",
            "choices", "transitions", "voiceIntentRefs", "npcResponseRefs", "offlineFallbackNodeId",
            "invalid_input_rules", "onEnterEffects", "onExitEffects", "injectionPoints", "terminal", "approvalStatus"
        };

        private static readonly string[] ChoiceProperties =
        {
            "id", "labelKey", "hintKey", "actionId", "nextNodeId", "entryConditions", "effects"
        };

        private static readonly string[] TransitionProperties =
        {
            "id", "triggerId", "nextNodeId", "entryConditions", "effects"
        };

        private static readonly string[] MediaProperties =
        {
            "schemaVersion", "id", "assetVersion", "mediaType", "contentHash", "uri", "language",
            "resolution", "durationMilliseconds", "dependencyRefs", "generationJobRef",
            "fallbackMediaRef", "approvalStatus"
        };

        private static readonly string[] VoiceIntentProperties =
        {
            "schemaVersion", "id", "displayKey", "topicKeys", "toneKey", "minimumConfidence",
            "npcResponseRef", "fallbackNpcResponseRef", "effects", "confirmationRule", "approvalStatus"
        };

        private static readonly string[] NpcResponseProperties =
        {
            "schemaVersion", "id", "textKey", "emotionKey", "lipSyncMediaRef", "audioMediaRef",
            "fallbackMediaRef", "effects", "nextNodeId", "approvalStatus"
        };

        private static readonly IReadOnlyDictionary<string, StoryInvalidInputKind> InvalidInputKinds =
            new Dictionary<string, StoryInvalidInputKind>(StringComparer.Ordinal)
            {
                { "abuse", StoryInvalidInputKind.Abuse },
                { "irrelevant", StoryInvalidInputKind.Irrelevant },
                { "too_long", StoryInvalidInputKind.TooLong },
                { "silence", StoryInvalidInputKind.Silence },
                { "low_confidence", StoryInvalidInputKind.LowConfidence }
            };

        public static StoryBundleLoadResult LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.IoFailure,
                    "剧情包路径为空，游戏无法启动。",
                    "Bundle path is empty.");
            }

            try
            {
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.IoFailure,
                    "未能读取剧情包，游戏无法安全启动。",
                    path + ": " + exception.Message);
            }
        }

        public static StoryBundleLoadResult Parse(string json)
        {
            StoryJsonValue root;
            try
            {
                root = StoryJson.Parse(json);
            }
            catch (Exception exception)
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.JsonMalformed,
                    "剧情包文件已损坏，无法解析。",
                    exception.Message);
            }

            if (root.Kind != StoryJsonKind.Object)
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.SchemaInvalid,
                    "剧情包结构不完整，无法安全启动。",
                    "/ must be a JSON object.");
            }

            string schemaVersion;
            try
            {
                schemaVersion = RequiredString(root, "schemaVersion", "/schemaVersion");
            }
            catch (StoryBundleContractException exception)
            {
                return SchemaFailure(exception);
            }

            if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.UnsupportedVersion,
                    "剧情包版本 " + schemaVersion + " 与当前游戏不兼容。",
                    "/schemaVersion expected " + SupportedSchemaVersion + " but found " + schemaVersion + ".");
            }

            StoryBundle bundle;
            try
            {
                bundle = BuildRuntimeBundle(root, schemaVersion);
            }
            catch (StoryBundleContractException exception)
            {
                return SchemaFailure(exception);
            }

            string expectedHash = bundle.Identity.ContentHash;
            string actualHash;
            try
            {
                actualHash = ComputeContentHash(root);
            }
            catch (Exception exception)
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.HashMismatch,
                    "剧情包哈希无法验证，游戏已停止加载。",
                    exception.Message);
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                return StoryBundleLoadResult.Failed(
                    StoryBundleErrorCode.HashMismatch,
                    "剧情包哈希不匹配，文件可能损坏或被修改。",
                    "/contentHash expected " + expectedHash + " but computed " + actualHash + ".");
            }

            return StoryBundleLoadResult.Succeeded(bundle);
        }

        public static string ComputeContentHash(string json)
        {
            return ComputeContentHash(StoryJson.Parse(json));
        }

        private static string ComputeContentHash(StoryJsonValue root)
        {
            if (root.Kind != StoryJsonKind.Object)
                throw new InvalidDataException("Bundle root must be an object.");
            byte[] canonical = StoryJson.SerializeCanonical(root, "contentHash");
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(canonical);
            }
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return "sha256:" + hex;
        }

        private static StoryBundle BuildRuntimeBundle(StoryJsonValue root, string schemaVersion)
        {
            EnsureAllowedProperties(root, RootProperties, "/");
            string bundleId = RequiredNonEmptyString(root, "id", "/id");
            string entryNodeId = RequiredNonEmptyString(root, "entryNodeId", "/entryNodeId");
            string contentHash = RequiredHash(root, "contentHash", "/contentHash");
            RequireExactString(root, "approvalStatus", "approved", "/approvalStatus");
            string distribution = RequiredNonEmptyString(root, "distributionStatus", "/distributionStatus");
            if (distribution != "internal_prototype_only" && distribution != "release_candidate" && distribution != "shipping")
                throw Contract("/distributionStatus", "contains unsupported value '" + distribution + "'.");

            StoryJsonValue localizationRoot = RequiredObject(root, "localizations", "/localizations");
            StoryJsonValue chinese = RequiredObject(localizationRoot, "zh-CN", "/localizations/zh-CN");
            var localized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StoryJsonValue> pair in chinese.ObjectValue)
            {
                if (pair.Value.Kind != StoryJsonKind.String)
                    throw Contract("/localizations/zh-CN/" + EscapePointer(pair.Key), "must be a string.");
                localized.Add(pair.Key, pair.Value.StringValue);
            }

            foreach (string arrayName in new[]
                     {
                         "storyFacts", "characterDefinitions", "characterStates", "questDefinitions",
                         "combatantStates", "encounterDefinitions",
                         "combatResolutions", "generationJobs", "saveGames"
                     })
            {
                RequireEmptyArray(RequiredArray(root, arrayName, "/" + arrayName), "/" + arrayName);
            }

            var mediaAssets = ParseMediaAssets(
                RequiredArray(root, "mediaAssets", "/mediaAssets"),
                schemaVersion);
            var npcResponses = ParseNpcResponses(
                RequiredArray(root, "npcResponses", "/npcResponses"),
                schemaVersion,
                localized,
                mediaAssets);
            var voiceIntents = ParseVoiceIntents(
                RequiredArray(root, "voiceIntents", "/voiceIntents"),
                schemaVersion,
                localized,
                npcResponses);
            var nodes = ParseNodes(
                RequiredArray(root, "sceneNodes", "/sceneNodes"),
                schemaVersion,
                localized,
                voiceIntents,
                npcResponses,
                mediaAssets);

            if (!nodes.ContainsKey(entryNodeId))
                throw Contract("/entryNodeId", "references unknown node '" + entryNodeId + "'.");

            foreach (StoryNode node in nodes.Values)
            {
                foreach (ChoiceDefinition choice in node.choices)
                {
                    if (!nodes.ContainsKey(choice.nextNodeId))
                        throw Contract("/sceneNodes/" + EscapePointer(node.id) + "/choices", "references unknown node '" + choice.nextNodeId + "'.");
                }
                foreach (StoryTransitionDefinition transition in node.transitions)
                {
                    if (!nodes.ContainsKey(transition.nextNodeId))
                        throw Contract("/sceneNodes/" + EscapePointer(node.id) + "/transitions", "references unknown node '" + transition.nextNodeId + "'.");
                }
                if (!string.IsNullOrEmpty(node.offlineFallbackNodeId) && !nodes.ContainsKey(node.offlineFallbackNodeId))
                    throw Contract("/sceneNodes/" + EscapePointer(node.id) + "/offlineFallbackNodeId", "references unknown node '" + node.offlineFallbackNodeId + "'.");
            }

            EnsureReachableGraph(nodes, entryNodeId);
            var identity = new StoryBundleIdentity(bundleId, schemaVersion, contentHash);
            return new StoryBundle(identity, entryNodeId, nodes, voiceIntents, npcResponses, mediaAssets);
        }

        private static Dictionary<string, StoryVoiceIntent> ParseVoiceIntents(
            StoryJsonValue array,
            string schemaVersion,
            IReadOnlyDictionary<string, string> localized,
            IReadOnlyDictionary<string, StoryNpcResponse> npcResponses)
        {
            var result = new Dictionary<string, StoryVoiceIntent>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string path = "/voiceIntents/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, path);
                EnsureAllowedProperties(item, VoiceIntentProperties, path);
                RequireExactString(item, "schemaVersion", schemaVersion, path + "/schemaVersion");
                string id = RequiredNonEmptyString(item, "id", path + "/id");
                if (id == "irrelevant" || id == "low_confidence")
                    throw Contract(path + "/id", "uses a reserved classifier outcome.");
                if (result.ContainsKey(id))
                    throw Contract(path + "/id", "duplicate intent id '" + id + "'.");

                string display = ResolveNonEmptyLocalization(item, "displayKey", localized, path);
                string tone = ResolveNonEmptyLocalization(item, "toneKey", localized, path);
                double threshold = RequiredFiniteUnitNumber(
                    item,
                    "minimumConfidence",
                    path + "/minimumConfidence");
                List<string> keywordPhrases = ResolveTopicPhrases(
                    RequiredArray(item, "topicKeys", path + "/topicKeys"),
                    localized,
                    path + "/topicKeys");
                string npcResponseRef = RequiredNonEmptyString(
                    item,
                    "npcResponseRef",
                    path + "/npcResponseRef");
                string fallbackNpcResponseRef = OptionalNonEmptyString(
                    item,
                    "fallbackNpcResponseRef",
                    path + "/fallbackNpcResponseRef");
                if (!npcResponses.ContainsKey(npcResponseRef))
                    throw Contract(path + "/npcResponseRef", "references unknown NPC response '" + npcResponseRef + "'.");
                if (!string.IsNullOrEmpty(fallbackNpcResponseRef) && !npcResponses.ContainsKey(fallbackNpcResponseRef))
                    throw Contract(path + "/fallbackNpcResponseRef", "references unknown NPC response '" + fallbackNpcResponseRef + "'.");
                RequireEmptyArray(RequiredArray(item, "effects", path + "/effects"), path + "/effects");
                if (item.TryGetProperty("confirmationRule", out StoryJsonValue confirmationRule))
                    ValidateConfirmationRule(confirmationRule, path + "/confirmationRule");
                RequireExactString(item, "approvalStatus", "approved", path + "/approvalStatus");

                result.Add(
                    id,
                    new StoryVoiceIntent(
                        id,
                        display,
                        keywordPhrases,
                        tone,
                        threshold,
                        npcResponseRef,
                        fallbackNpcResponseRef));
            }
            return result;
        }

        private static Dictionary<string, StoryNpcResponse> ParseNpcResponses(
            StoryJsonValue array,
            string schemaVersion,
            IReadOnlyDictionary<string, string> localized,
            IReadOnlyDictionary<string, StoryMediaAsset> mediaAssets)
        {
            var result = new Dictionary<string, StoryNpcResponse>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string path = "/npcResponses/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, path);
                EnsureAllowedProperties(item, NpcResponseProperties, path);
                RequireExactString(item, "schemaVersion", schemaVersion, path + "/schemaVersion");
                string id = RequiredNonEmptyString(item, "id", path + "/id");
                string text = ResolveNonEmptyLocalization(item, "textKey", localized, path);
                string emotion = ResolveNonEmptyLocalization(item, "emotionKey", localized, path);
                string lipSync = RequiredNonEmptyString(item, "lipSyncMediaRef", path + "/lipSyncMediaRef");
                string audio = OptionalNonEmptyString(item, "audioMediaRef", path + "/audioMediaRef");
                string fallback = OptionalNonEmptyString(item, "fallbackMediaRef", path + "/fallbackMediaRef");
                ValidateMediaReference(lipSync, mediaAssets, path + "/lipSyncMediaRef");
                ValidateOptionalMediaReference(audio, mediaAssets, path + "/audioMediaRef");
                ValidateOptionalMediaReference(fallback, mediaAssets, path + "/fallbackMediaRef");
                RequireEmptyArray(RequiredArray(item, "effects", path + "/effects"), path + "/effects");
                if (item.TryGetProperty("nextNodeId", out _))
                    throw Contract(path + "/nextNodeId", "is not executable by the v1 offline client; use a fixed choice instead.");
                RequireExactString(item, "approvalStatus", "approved", path + "/approvalStatus");
                if (result.ContainsKey(id)) throw Contract(path + "/id", "duplicate NPC response id '" + id + "'.");
                result.Add(id, new StoryNpcResponse(id, text, emotion, lipSync, audio, fallback));
            }
            return result;
        }

        private static Dictionary<string, StoryMediaAsset> ParseMediaAssets(
            StoryJsonValue array,
            string schemaVersion)
        {
            var result = new Dictionary<string, StoryMediaAsset>(StringComparer.Ordinal);
            var fallbackRefs = new List<KeyValuePair<string, string>>();
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string path = "/mediaAssets/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, path);
                EnsureAllowedProperties(item, MediaProperties, path);
                RequireExactString(item, "schemaVersion", schemaVersion, path + "/schemaVersion");
                string id = RequiredNonEmptyString(item, "id", path + "/id");
                RequiredNonEmptyString(item, "assetVersion", path + "/assetVersion");
                string mediaType = RequiredNonEmptyString(item, "mediaType", path + "/mediaType");
                if (mediaType != "video" && mediaType != "audio" && mediaType != "image" &&
                    mediaType != "animation" && mediaType != "subtitle")
                    throw Contract(path + "/mediaType", "contains unsupported value '" + mediaType + "'.");
                string hash = RequiredHash(item, "contentHash", path + "/contentHash");
                string uri = RequiredNonEmptyString(item, "uri", path + "/uri");
                RequiredNullOrString(item, "language", path + "/language");
                if (item.TryGetProperty("resolution", out StoryJsonValue resolution))
                {
                    RequireKind(resolution, StoryJsonKind.Object, path + "/resolution");
                    EnsureAllowedProperties(resolution, new[] { "width", "height" }, path + "/resolution");
                    if (RequiredInteger(resolution, "width", path + "/resolution/width") < 1 ||
                        RequiredInteger(resolution, "height", path + "/resolution/height") < 1)
                        throw Contract(path + "/resolution", "width and height must be positive integers.");
                }
                if (item.TryGetProperty("durationMilliseconds", out StoryJsonValue duration))
                {
                    int milliseconds = IntegerValue(duration, path + "/durationMilliseconds");
                    if (milliseconds < 0) throw Contract(path + "/durationMilliseconds", "must not be negative.");
                }
                RequireUniqueStringArray(RequiredArray(item, "dependencyRefs", path + "/dependencyRefs"), path + "/dependencyRefs");
                OptionalNonEmptyString(item, "generationJobRef", path + "/generationJobRef");
                string fallback = OptionalString(item, "fallbackMediaRef", path + "/fallbackMediaRef");
                RequireExactString(item, "approvalStatus", "approved", path + "/approvalStatus");
                if (!uri.StartsWith("unity-resource:///", StringComparison.Ordinal) &&
                    !uri.StartsWith("streaming-assets:///", StringComparison.Ordinal))
                    throw Contract(path + "/uri", "uses unsupported runtime URI '" + uri + "'.");
                if (result.ContainsKey(id)) throw Contract(path + "/id", "duplicate media id '" + id + "'.");
                result.Add(id, new StoryMediaAsset(id, mediaType, uri, hash));
                if (!string.IsNullOrEmpty(fallback))
                    fallbackRefs.Add(new KeyValuePair<string, string>(path + "/fallbackMediaRef", fallback));
            }
            if (result.Count == 0) throw Contract("/mediaAssets", "must contain at least one asset.");
            foreach (KeyValuePair<string, string> fallback in fallbackRefs)
            {
                if (!result.ContainsKey(fallback.Value))
                    throw Contract(fallback.Key, "references unknown media id '" + fallback.Value + "'.");
            }
            return result;
        }

        private static Dictionary<string, StoryNode> ParseNodes(
            StoryJsonValue array,
            string schemaVersion,
            IReadOnlyDictionary<string, string> localized,
            IReadOnlyDictionary<string, StoryVoiceIntent> voiceIntents,
            IReadOnlyDictionary<string, StoryNpcResponse> npcResponses,
            IReadOnlyDictionary<string, StoryMediaAsset> mediaAssets)
        {
            var result = new Dictionary<string, StoryNode>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string path = "/sceneNodes/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, path);
                EnsureAllowedProperties(item, NodeProperties, path);
                RequireExactString(item, "schemaVersion", schemaVersion, path + "/schemaVersion");
                string id = RequiredNonEmptyString(item, "id", path + "/id");
                RequiredNonEmptyString(item, "displayKey", path + "/displayKey");
                string title = Resolve(item, "titleKey", localized, path);
                string location = Resolve(item, "locationKey", localized, path);
                string speaker = Resolve(item, "speakerKey", localized, path);
                string maleText = Resolve(item, "maleTextKey", localized, path);
                string femaleText = Resolve(item, "femaleTextKey", localized, path);
                string kindName = RequiredNonEmptyString(item, "nodeKind", path + "/nodeKind");
                if (!Enum.TryParse(kindName, false, out NodeKind kind) || !Enum.IsDefined(typeof(NodeKind), kind))
                    throw Contract(path + "/nodeKind", "contains unsupported value '" + kindName + "'.");
                int estimatedMinutes = RequiredInteger(item, "estimatedMinutes", path + "/estimatedMinutes");
                if (estimatedMinutes <= 0) throw Contract(path + "/estimatedMinutes", "must be greater than zero.");
                RequireEmptyArray(RequiredArray(item, "entryConditions", path + "/entryConditions"), path + "/entryConditions");
                StoryJsonValue mediaRefs = RequiredArray(item, "mediaRefs", path + "/mediaRefs");
                RequireUniqueStringArray(mediaRefs, path + "/mediaRefs");
                string fallback = OptionalString(item, "fallbackMediaRef", path + "/fallbackMediaRef");
                string background = RequiredNonEmptyString(item, "backgroundMediaRef", path + "/backgroundMediaRef");
                string portrait = OptionalString(item, "portraitMediaRef", path + "/portraitMediaRef");
                string intro = OptionalString(item, "introMediaRef", path + "/introMediaRef");
                StoryJsonValue voiceIntentRefs = RequiredArray(item, "voiceIntentRefs", path + "/voiceIntentRefs");
                RequireUniqueStringArray(voiceIntentRefs, path + "/voiceIntentRefs");
                StoryJsonValue npcResponseRefs = null;
                if (item.TryGetProperty("npcResponseRefs", out StoryJsonValue responseRefs))
                {
                    npcResponseRefs = RequireKind(responseRefs, StoryJsonKind.Array, path + "/npcResponseRefs");
                    RequireUniqueStringArray(npcResponseRefs, path + "/npcResponseRefs");
                }
                string offlineFallback = OptionalString(item, "offlineFallbackNodeId", path + "/offlineFallbackNodeId");
                if (item.TryGetProperty("onEnterEffects", out StoryJsonValue onEnter))
                    RequireEmptyArray(RequireKind(onEnter, StoryJsonKind.Array, path + "/onEnterEffects"), path + "/onEnterEffects");
                if (item.TryGetProperty("onExitEffects", out StoryJsonValue onExit))
                    RequireEmptyArray(RequireKind(onExit, StoryJsonKind.Array, path + "/onExitEffects"), path + "/onExitEffects");
                ValidateInjectionPoints(RequiredArray(item, "injectionPoints", path + "/injectionPoints"), path + "/injectionPoints");
                bool terminal = RequiredBoolean(item, "terminal", path + "/terminal");
                RequireExactString(item, "approvalStatus", "approved", path + "/approvalStatus");
                if (terminal != (kind == NodeKind.Ending))
                    throw Contract(path + "/terminal", "must agree with nodeKind Ending.");

                ValidateMediaReference(background, mediaAssets, path + "/backgroundMediaRef");
                ValidateOptionalMediaReference(fallback, mediaAssets, path + "/fallbackMediaRef");
                ValidateOptionalMediaReference(portrait, mediaAssets, path + "/portraitMediaRef");
                ValidateOptionalMediaReference(intro, mediaAssets, path + "/introMediaRef");
                foreach (StoryJsonValue mediaRef in mediaRefs.ArrayValue)
                    ValidateMediaReference(mediaRef.StringValue, mediaAssets, path + "/mediaRefs");

                var node = new StoryNode
                {
                    id = id,
                    title = title,
                    location = location,
                    speaker = speaker,
                    maleText = maleText,
                    femaleText = femaleText,
                    background = background,
                    portrait = portrait,
                    introMediaRef = intro,
                    fallbackMediaRef = fallback,
                    offlineFallbackNodeId = offlineFallback,
                    kind = kind,
                    estimatedMinutes = estimatedMinutes
                };
                for (int refIndex = 0; refIndex < voiceIntentRefs.ArrayValue.Count; refIndex++)
                {
                    string intentId = voiceIntentRefs.ArrayValue[refIndex].StringValue;
                    string refPath = path + "/voiceIntentRefs/" + refIndex.ToString(CultureInfo.InvariantCulture);
                    if (!voiceIntents.ContainsKey(intentId))
                        throw Contract(refPath, "references unknown intent '" + intentId + "'.");
                    node.voiceIntentRefs.Add(intentId);
                }
                if (npcResponseRefs != null)
                {
                    for (int responseIndex = 0; responseIndex < npcResponseRefs.ArrayValue.Count; responseIndex++)
                    {
                        string responseId = npcResponseRefs.ArrayValue[responseIndex].StringValue;
                        string responsePath = path + "/npcResponseRefs/" + responseIndex.ToString(CultureInfo.InvariantCulture);
                        if (!npcResponses.ContainsKey(responseId))
                            throw Contract(responsePath, "references unknown NPC response '" + responseId + "'.");
                        node.npcResponseRefs.Add(responseId);
                    }
                }
                if (item.TryGetProperty("invalid_input_rules", out StoryJsonValue invalidRulesValue))
                {
                    StoryJsonValue invalidRules = RequireKind(invalidRulesValue, StoryJsonKind.Object, path + "/invalid_input_rules");
                    EnsureAllowedProperties(invalidRules, InvalidInputKinds.Keys, path + "/invalid_input_rules");
                    foreach (KeyValuePair<string, StoryInvalidInputKind> invalidKind in InvalidInputKinds)
                    {
                        string rulePath = path + "/invalid_input_rules/" + invalidKind.Key;
                        StoryJsonValue rule = RequiredObject(invalidRules, invalidKind.Key, rulePath);
                        EnsureAllowedProperties(rule, new[] { "npcResponseRef" }, rulePath);
                        string responseId = RequiredNonEmptyString(rule, "npcResponseRef", rulePath + "/npcResponseRef");
                        if (!npcResponses.ContainsKey(responseId))
                            throw Contract(rulePath + "/npcResponseRef", "references unknown NPC response '" + responseId + "'.");
                        if (!node.npcResponseRefs.Contains(responseId))
                            throw Contract(rulePath + "/npcResponseRef", "is outside this node's npcResponseRefs allowlist.");
                        node.invalidInputRules.Add(invalidKind.Value, responseId);
                    }
                }
                else if (node.voiceIntentRefs.Count > 0)
                {
                    throw Contract(path + "/invalid_input_rules", "is required for a voice-enabled node.");
                }
                foreach (string intentId in node.voiceIntentRefs)
                {
                    StoryVoiceIntent intent = voiceIntents[intentId];
                    if (!node.npcResponseRefs.Contains(intent.NpcResponseRef))
                        throw Contract(path + "/voiceIntentRefs", "allows an intent whose NPC response is outside npcResponseRefs.");
                    if (!string.IsNullOrEmpty(intent.FallbackNpcResponseRef) &&
                        !node.npcResponseRefs.Contains(intent.FallbackNpcResponseRef))
                        throw Contract(path + "/voiceIntentRefs", "allows an intent whose fallback response is outside npcResponseRefs.");
                }
                ParseChoices(RequiredArray(item, "choices", path + "/choices"), localized, node, path + "/choices");
                ParseTransitions(RequiredArray(item, "transitions", path + "/transitions"), node, path + "/transitions");
                if (result.ContainsKey(id)) throw Contract(path + "/id", "duplicate node id '" + id + "'.");
                result.Add(id, node);
            }
            if (result.Count == 0) throw Contract("/sceneNodes", "must contain at least one node.");
            if (!result.Values.Any(node => node.kind == NodeKind.Ending))
                throw Contract("/sceneNodes", "must contain at least one ending node.");
            return result;
        }

        private static void ParseChoices(
            StoryJsonValue array,
            IReadOnlyDictionary<string, string> localized,
            StoryNode node,
            string path)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var actions = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string itemPath = path + "/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, itemPath);
                EnsureAllowedProperties(item, ChoiceProperties, itemPath);
                string id = RequiredNonEmptyString(item, "id", itemPath + "/id");
                string label = Resolve(item, "labelKey", localized, itemPath);
                string hint = Resolve(item, "hintKey", localized, itemPath);
                string action = RequiredNonEmptyString(item, "actionId", itemPath + "/actionId");
                string next = RequiredNonEmptyString(item, "nextNodeId", itemPath + "/nextNodeId");
                RequireEmptyArray(RequiredArray(item, "entryConditions", itemPath + "/entryConditions"), itemPath + "/entryConditions");
                RequireEmptyArray(RequiredArray(item, "effects", itemPath + "/effects"), itemPath + "/effects");
                if (!ids.Add(id)) throw Contract(itemPath + "/id", "duplicate choice id '" + id + "'.");
                if (!actions.Add(action)) throw Contract(itemPath + "/actionId", "duplicate choice action '" + action + "'.");
                node.choices.Add(new ChoiceDefinition(label, action, hint, next));
            }
        }

        private static void ParseTransitions(StoryJsonValue array, StoryNode node, string path)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var triggers = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string itemPath = path + "/" + index.ToString(CultureInfo.InvariantCulture);
                StoryJsonValue item = RequireKind(array.ArrayValue[index], StoryJsonKind.Object, itemPath);
                EnsureAllowedProperties(item, TransitionProperties, itemPath);
                string id = RequiredNonEmptyString(item, "id", itemPath + "/id");
                string trigger = RequiredNonEmptyString(item, "triggerId", itemPath + "/triggerId");
                string next = RequiredNonEmptyString(item, "nextNodeId", itemPath + "/nextNodeId");
                RequireEmptyArray(RequiredArray(item, "entryConditions", itemPath + "/entryConditions"), itemPath + "/entryConditions");
                RequireEmptyArray(RequiredArray(item, "effects", itemPath + "/effects"), itemPath + "/effects");
                if (!ids.Add(id)) throw Contract(itemPath + "/id", "duplicate transition id '" + id + "'.");
                if (!triggers.Add(trigger)) throw Contract(itemPath + "/triggerId", "duplicate trigger '" + trigger + "'.");
                node.transitions.Add(new StoryTransitionDefinition(trigger, next));
            }
        }

        private static void EnsureReachableGraph(IReadOnlyDictionary<string, StoryNode> nodes, string entryNodeId)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal) { entryNodeId };
            var pending = new Queue<string>();
            pending.Enqueue(entryNodeId);
            while (pending.Count > 0)
            {
                StoryNode node = nodes[pending.Dequeue()];
                foreach (string next in node.choices.Select(choice => choice.nextNodeId)
                             .Concat(node.transitions.Select(transition => transition.nextNodeId)))
                {
                    if (reachable.Add(next)) pending.Enqueue(next);
                }
            }
            if (reachable.Count == nodes.Count) return;
            string missing = string.Join(", ", nodes.Keys.Where(id => !reachable.Contains(id)).OrderBy(id => id, StringComparer.Ordinal));
            throw Contract("/sceneNodes", "contains nodes unreachable from entryNodeId: " + missing + ".");
        }

        private static string Resolve(
            StoryJsonValue owner,
            string keyProperty,
            IReadOnlyDictionary<string, string> localized,
            string path)
        {
            string key = RequiredNonEmptyString(owner, keyProperty, path + "/" + keyProperty);
            if (!localized.TryGetValue(key, out string value))
                throw Contract(path + "/" + keyProperty, "references missing zh-CN key '" + key + "'.");
            return value;
        }

        private static string ResolveNonEmptyLocalization(
            StoryJsonValue owner,
            string keyProperty,
            IReadOnlyDictionary<string, string> localized,
            string path)
        {
            string keyPath = path + "/" + keyProperty;
            string key = RequiredNonEmptyString(owner, keyProperty, keyPath);
            if (!localized.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
                throw Contract(keyPath, "references a missing or empty zh-CN localization '" + key + "'.");
            return value.Trim();
        }

        private static List<string> ResolveTopicPhrases(
            StoryJsonValue array,
            IReadOnlyDictionary<string, string> localized,
            string path)
        {
            RequireUniqueStringArray(array, path);
            var phrases = new List<string>();
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string itemPath = path + "/" + index.ToString(CultureInfo.InvariantCulture);
                string key = array.ArrayValue[index].StringValue;
                if (!localized.TryGetValue(key, out string phrase) || string.IsNullOrWhiteSpace(phrase))
                    throw Contract(itemPath, "references a missing or empty zh-CN localization '" + key + "'.");
                string cleaned = phrase.Trim().Normalize(NormalizationForm.FormKC);
                if (!normalized.Add(cleaned))
                    throw Contract(itemPath, "resolves to duplicate topic phrase '" + cleaned + "'.");
                phrases.Add(cleaned);
            }
            if (phrases.Count == 0)
                throw Contract(path, "must resolve at least one zh-CN topic phrase.");
            return phrases;
        }

        private static void ValidateConfirmationRule(StoryJsonValue value, string path)
        {
            StoryJsonValue rule = RequireKind(value, StoryJsonKind.Object, path);
            string mode = RequiredNonEmptyString(rule, "mode", path + "/mode");
            if (mode == "high_confidence")
            {
                EnsureAllowedProperties(rule, new[] { "mode", "minimumConfidence" }, path);
                RequiredFiniteUnitNumber(rule, "minimumConfidence", path + "/minimumConfidence");
                return;
            }
            if (mode == "npc_second_confirmation")
            {
                EnsureAllowedProperties(rule, new[] { "mode", "npcResponseRef" }, path);
                RequiredNonEmptyString(rule, "npcResponseRef", path + "/npcResponseRef");
                return;
            }
            throw Contract(path + "/mode", "contains unsupported value '" + mode + "'.");
        }

        private static void ValidateOptionalMediaReference(
            string id,
            IReadOnlyDictionary<string, StoryMediaAsset> media,
            string path)
        {
            if (!string.IsNullOrEmpty(id)) ValidateMediaReference(id, media, path);
        }

        private static void ValidateMediaReference(
            string id,
            IReadOnlyDictionary<string, StoryMediaAsset> media,
            string path)
        {
            if (!media.ContainsKey(id)) throw Contract(path, "references unknown media id '" + id + "'.");
        }

        private static StoryBundleLoadResult SchemaFailure(StoryBundleContractException exception)
        {
            return StoryBundleLoadResult.Failed(
                StoryBundleErrorCode.SchemaInvalid,
                "剧情包结构不完整或引用无效，无法安全启动。",
                exception.Message);
        }

        private static void EnsureAllowedProperties(StoryJsonValue owner, IEnumerable<string> allowed, string path)
        {
            var set = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (string property in owner.ObjectValue.Keys)
            {
                if (!set.Contains(property))
                    throw Contract(path + (path == "/" ? string.Empty : "/") + EscapePointer(property), "is not allowed by schema.");
            }
        }

        private static string RequiredHash(StoryJsonValue owner, string property, string path)
        {
            string value = RequiredString(owner, property, path);
            if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
                throw Contract(path, "must be 'sha256:' followed by 64 lowercase hexadecimal characters.");
            for (int index = 7; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    throw Contract(path, "must be 'sha256:' followed by 64 lowercase hexadecimal characters.");
            }
            return value;
        }

        private static void RequireExactString(
            StoryJsonValue owner,
            string property,
            string expected,
            string path)
        {
            string value = RequiredString(owner, property, path);
            if (!string.Equals(value, expected, StringComparison.Ordinal))
                throw Contract(path, "expected '" + expected + "' but found '" + value + "'.");
        }

        private static StoryJsonValue RequiredObject(StoryJsonValue owner, string property, string path)
        {
            return RequireKind(RequiredProperty(owner, property, path), StoryJsonKind.Object, path);
        }

        private static StoryJsonValue RequiredArray(StoryJsonValue owner, string property, string path)
        {
            return RequireKind(RequiredProperty(owner, property, path), StoryJsonKind.Array, path);
        }

        private static string RequiredString(StoryJsonValue owner, string property, string path)
        {
            StoryJsonValue value = RequireKind(RequiredProperty(owner, property, path), StoryJsonKind.String, path);
            return value.StringValue;
        }

        private static string RequiredNonEmptyString(StoryJsonValue owner, string property, string path)
        {
            string value = RequiredString(owner, property, path);
            if (string.IsNullOrWhiteSpace(value)) throw Contract(path, "must not be empty.");
            return value;
        }

        private static string OptionalString(StoryJsonValue owner, string property, string path)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value)) return string.Empty;
            return RequireKind(value, StoryJsonKind.String, path).StringValue;
        }

        private static string OptionalNonEmptyString(StoryJsonValue owner, string property, string path)
        {
            string value = OptionalString(owner, property, path);
            if (owner.TryGetProperty(property, out _) && string.IsNullOrWhiteSpace(value))
                throw Contract(path, "must not be empty.");
            return value;
        }

        private static void RequiredNullOrString(StoryJsonValue owner, string property, string path)
        {
            StoryJsonValue value = RequiredProperty(owner, property, path);
            if (value.Kind != StoryJsonKind.Null && value.Kind != StoryJsonKind.String)
                throw Contract(path, "must be null or a string.");
            if (value.Kind == StoryJsonKind.String && value.StringValue.Length < 2)
                throw Contract(path, "must contain at least two characters when present.");
        }

        private static int RequiredInteger(StoryJsonValue owner, string property, string path)
        {
            return IntegerValue(RequiredProperty(owner, property, path), path);
        }

        private static double RequiredFiniteUnitNumber(StoryJsonValue owner, string property, string path)
        {
            StoryJsonValue value = RequireKind(RequiredProperty(owner, property, path), StoryJsonKind.Number, path);
            if (!double.TryParse(
                    value.NumberToken,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double result) ||
                double.IsNaN(result) ||
                double.IsInfinity(result) ||
                result < 0d ||
                result > 1d)
            {
                throw Contract(path, "must be a finite number from zero through one.");
            }
            return result;
        }

        private static int IntegerValue(StoryJsonValue value, string path)
        {
            RequireKind(value, StoryJsonKind.Number, path);
            if (!int.TryParse(value.NumberToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw Contract(path, "must be a 32-bit integer.");
            return result;
        }

        private static bool RequiredBoolean(StoryJsonValue owner, string property, string path)
        {
            return RequireKind(RequiredProperty(owner, property, path), StoryJsonKind.Boolean, path).BooleanValue;
        }

        private static StoryJsonValue RequiredProperty(StoryJsonValue owner, string property, string path)
        {
            if (owner.Kind != StoryJsonKind.Object || !owner.TryGetProperty(property, out StoryJsonValue value))
                throw Contract(path, "is required.");
            return value;
        }

        private static StoryJsonValue RequireKind(StoryJsonValue value, StoryJsonKind expected, string path)
        {
            if (value.Kind != expected) throw Contract(path, "must be " + expected.ToString().ToLowerInvariant() + ".");
            return value;
        }

        private static void RequireUniqueStringArray(StoryJsonValue array, string path)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.ArrayValue.Count; index++)
            {
                string itemPath = path + "/" + index.ToString(CultureInfo.InvariantCulture);
                string value = RequireKind(array.ArrayValue[index], StoryJsonKind.String, itemPath).StringValue;
                if (string.IsNullOrWhiteSpace(value)) throw Contract(itemPath, "must not be empty.");
                if (!unique.Add(value)) throw Contract(itemPath, "duplicates '" + value + "'.");
            }
        }

        private static void RequireEmptyArray(StoryJsonValue array, string path)
        {
            if (array.ArrayValue.Count != 0)
                throw Contract(path, "contains v1 records that this offline runtime does not consume; rebuild with an empty collection.");
        }

        private static void ValidateInjectionPoints(StoryJsonValue array, string path)
        {
            RequireUniqueStringArray(array, path);
            foreach (StoryJsonValue value in array.ArrayValue)
            {
                if (value.StringValue != "node_intro" && value.StringValue != "travel_event" && value.StringValue != "npc_mention")
                    throw Contract(path, "contains unsupported injection point '" + value.StringValue + "'.");
            }
        }

        private static StoryBundleContractException Contract(string path, string message)
        {
            return new StoryBundleContractException(path + " " + message);
        }

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private sealed class StoryBundleContractException : Exception
        {
            public StoryBundleContractException(string message) : base(message)
            {
            }
        }
    }
}
