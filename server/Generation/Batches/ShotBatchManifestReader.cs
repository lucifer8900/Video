using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Batches;

public sealed class ShotBatchManifestReader
{
    private const string ProjectAspectRatio = "16:9";
    private static readonly string[] RequiredRootProperties =
    [
        "schemaVersion",
        "manifestId",
        "sourceBundleId",
        "sourceBundleContentHash",
        "language",
        "currency",
        "approvalStatus",
        "shotDispatchAllowed",
        "responseRequirements",
        "shots",
        "contentHash",
    ];
    private static readonly string[] RequiredShotProperties =
    [
        "shotId", "nodeId", "nodeKind", "source", "dialogueVariants",
        "dialogueBinding", "mediaRoles", "npcResponseRefs",
        "targetDurationMilliseconds", "firstFrame", "lastFrame", "primaryMedia",
        "generationTier", "maximumCostMicros", "fallbackMedia", "fallbackOrigin",
        "characterStateRefs", "readinessStatus", "issueCodes", "dispatchAllowed",
    ];
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<GenerationBatchManifest> ReadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The shot manifest stream must be readable.", nameof(source));

        JsonNode? parsed;
        try
        {
            parsed = await JsonNode.ParseAsync(
                source,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw Invalid("manifest.json", "The shot manifest is not valid JSON.", exception);
        }

        JsonObject root = parsed as JsonObject
            ?? throw Invalid("manifest.shape", "The shot manifest root must be an object.");
        EnsureExactRootShape(root);
        string actualContentHash = RequiredString(root, "contentHash");
        if (!Sha256Pattern.IsMatch(actualContentHash))
            throw Invalid("manifest.content_hash", "The shot manifest content hash is invalid.");
        JsonObject content = root.DeepClone().AsObject();
        content.Remove("contentHash");
        string expectedContentHash = Hash(SerializeCanonical(content));
        if (!string.Equals(actualContentHash, expectedContentHash, StringComparison.Ordinal))
            throw Invalid("manifest.content_hash", "The shot manifest content hash does not match its content.");

        if (!string.Equals(RequiredString(root, "schemaVersion"), "1.0.0", StringComparison.Ordinal))
            throw Invalid("manifest.schema_version", "The shot manifest schema version is unsupported.");
        string manifestId = RequiredString(root, "manifestId");
        string currency = RequiredString(root, "currency");
        GenerationBatchManifestApproval approval = RequiredString(root, "approvalStatus") switch
        {
            "approved" => GenerationBatchManifestApproval.Approved,
            "needs_review" => GenerationBatchManifestApproval.NeedsReview,
            _ => throw Invalid("manifest.approval_status", "The shot manifest approval status is invalid."),
        };
        bool dispatchAllowed = RequiredBoolean(root, "shotDispatchAllowed");
        JsonArray shots = root["shots"] as JsonArray
            ?? throw Invalid("manifest.shots", "The shot manifest shots property must be an array.");
        if (shots.Count == 0)
            throw Invalid("manifest.shots", "The shot manifest must contain at least one shot.");

        var mapped = new List<GenerationBatchShot>(shots.Count);
        var shotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in shots)
        {
            JsonObject shot = node as JsonObject
                ?? throw Invalid("manifest.shot", "Each shot must be an object.");
            EnsureShotContract(shot);
            string shotId = RequiredString(shot, "shotId");
            if (!shotIds.Add(shotId))
                throw Invalid("manifest.duplicate_shot", "The shot manifest contains duplicate shot IDs.");
            string tierValue = RequiredString(shot, "generationTier");
            bool shotDispatchAllowed = RequiredBoolean(shot, "dispatchAllowed");
            GenerationBatchTier tier = tierValue switch
            {
                "preview_fast" => GenerationBatchTier.PreviewFast,
                "final_quality" => GenerationBatchTier.FinalQuality,
                "reuse_only" or "blocked_pending_g2" when !shotDispatchAllowed =>
                    GenerationBatchTier.PreviewFast,
                _ => throw Invalid("manifest.generation_tier", "The shot generation tier is invalid."),
            };
            GenerationBatchShotReadiness readiness = RequiredString(shot, "readinessStatus") switch
            {
                "ready" => GenerationBatchShotReadiness.Ready,
                "needs_review" => GenerationBatchShotReadiness.NeedsReview,
                "blocked" => GenerationBatchShotReadiness.Blocked,
                _ => throw Invalid("manifest.readiness", "The shot readiness status is invalid."),
            };
            int duration = OptionalPositiveInt32(shot, "targetDurationMilliseconds");
            long maximumCost = RequiredNonNegativeInt64(shot, "maximumCostMicros");
            string inputHash = Hash(SerializeCanonical(shot));
            string dialogueBinding = RequiredString(shot, "dialogueBinding");
            GenerationBatchFramePin? firstFrame = MapFrame(shot["firstFrame"]);
            GenerationBatchFramePin? lastFrame = MapFrame(shot["lastFrame"]);
            GenerationBatchMediaPin? primaryMedia = MapMediaPin(shot["primaryMedia"]);
            GenerationBatchMediaPin fallbackMedia = MapMediaPin(shot["fallbackMedia"])
                ?? throw Invalid("manifest.shot_contract", "A fallback media pin is required.");
            mapped.Add(new GenerationBatchShot(
                shotId,
                inputHash,
                shotDispatchAllowed,
                readiness,
                tier,
                duration,
                ProjectAspectRatio,
                maximumCost,
                dialogueBinding,
                firstFrame,
                lastFrame,
                primaryMedia,
                fallbackMedia));
        }

        return new GenerationBatchManifest(
            manifestId,
            actualContentHash,
            approval,
            dispatchAllowed,
            currency,
            mapped);
    }

    private static void EnsureExactRootShape(JsonObject root)
    {
        string[] actual = root.Select(property => property.Key).Order(StringComparer.Ordinal).ToArray();
        string[] expected = RequiredRootProperties.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw Invalid("manifest.shape", "The shot manifest root properties do not match the contract.");
    }

    private static void EnsureShotContract(JsonObject shot)
    {
        EnsureExactProperties(shot, RequiredShotProperties, "manifest.shot_contract");
        _ = RequiredString(shot, "shotId");
        _ = RequiredString(shot, "nodeId");
        _ = RequiredString(shot, "nodeKind");
        JsonObject source = RequiredObject(shot, "source", "manifest.shot_contract");
        EnsureAllowedProperties(
            source,
            ["sourceId", "locator", "evidenceHash", "needsReview"],
            "manifest.shot_contract");
        _ = RequiredString(source, "sourceId");
        _ = RequiredString(source, "locator");
        if (source["evidenceHash"] is not null &&
            !Sha256Pattern.IsMatch(RequiredString(source, "evidenceHash")))
        {
            throw Invalid("manifest.shot_contract", "The shot source evidence hash is invalid.");
        }

        JsonArray dialogueVariants = RequiredArray(shot, "dialogueVariants", "manifest.shot_contract");
        if (dialogueVariants.Count != 2)
            throw Invalid("manifest.shot_contract", "A shot requires male and female dialogue variants.");
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? variantNode in dialogueVariants)
        {
            JsonObject variant = variantNode as JsonObject
                ?? throw Invalid("manifest.shot_contract", "A dialogue variant must be an object.");
            EnsureExactProperties(
                variant,
                ["route", "localizationKey", "text"],
                "manifest.shot_contract");
            string route = RequiredString(variant, "route");
            if (route is not ("male" or "female") || !routes.Add(route))
                throw Invalid("manifest.shot_contract", "Dialogue routes must be one male and one female.");
            _ = RequiredString(variant, "localizationKey");
            _ = RequiredString(variant, "text");
        }

        JsonObject mediaRoles = RequiredObject(shot, "mediaRoles", "manifest.shot_contract");
        EnsureExactProperties(
            mediaRoles,
            [
                "backgroundMediaRef", "portraitMediaRef", "introMediaRef",
                "explicitFallbackMediaRef", "auxiliaryMediaRefs",
            ],
            "manifest.shot_contract");
        _ = RequiredString(mediaRoles, "backgroundMediaRef");
        EnsureNullableString(mediaRoles, "portraitMediaRef");
        EnsureNullableString(mediaRoles, "introMediaRef");
        EnsureNullableString(mediaRoles, "explicitFallbackMediaRef");
        EnsureStringArray(mediaRoles, "auxiliaryMediaRefs", allowEmpty: true);
        EnsureStringArray(shot, "npcResponseRefs", allowEmpty: true);
        EnsureStringArray(shot, "characterStateRefs", allowEmpty: true);
        EnsureStringArray(shot, "issueCodes", allowEmpty: true);
        ValidateOptionalFrame(shot["firstFrame"]);
        ValidateOptionalFrame(shot["lastFrame"]);
        ValidateOptionalMediaPin(shot["primaryMedia"]);
        ValidateMediaPin(
            RequiredObject(shot, "fallbackMedia", "manifest.shot_contract"),
            ["video", "animation", "image", "audio", "subtitle"]);
        _ = RequiredString(shot, "fallbackOrigin");
        _ = RequiredString(shot, "dialogueBinding");

        bool dispatchAllowed = RequiredBoolean(shot, "dispatchAllowed");
        if (!dispatchAllowed) return;

        if (source["needsReview"] is not JsonValue needsReviewValue ||
            !needsReviewValue.TryGetValue(out bool needsReview) ||
            needsReview)
        {
            throw Invalid("manifest.shot_contract", "A dispatchable shot requires an approved source.");
        }

        if (!string.Equals(
                RequiredString(shot, "dialogueBinding"),
                "bound_to_primary_media",
                StringComparison.Ordinal) ||
            OptionalPositiveInt32(shot, "targetDurationMilliseconds") <= 0 ||
            !string.Equals(RequiredString(shot, "readinessStatus"), "ready", StringComparison.Ordinal) ||
            RequiredNonNegativeInt64(shot, "maximumCostMicros") <= 0 ||
            RequiredArray(shot, "issueCodes", "manifest.shot_contract").Count != 0 ||
            RequiredArray(shot, "characterStateRefs", "manifest.shot_contract").Count == 0)
        {
            throw Invalid("manifest.shot_contract", "A dispatchable shot does not satisfy ready-state requirements.");
        }

        ValidateApprovedFrame(RequiredObject(shot, "firstFrame", "manifest.shot_contract"));
        ValidateApprovedFrame(RequiredObject(shot, "lastFrame", "manifest.shot_contract"));
        ValidateMediaPin(
            RequiredObject(shot, "primaryMedia", "manifest.shot_contract"),
            ["video", "animation"]);
    }

    private static void ValidateOptionalFrame(JsonNode? node)
    {
        if (node is null) return;
        ValidateFrame(node as JsonObject
            ?? throw Invalid("manifest.shot_contract", "A frame reference must be an object or null."));
    }

    private static void ValidateApprovedFrame(JsonObject frame)
    {
        ValidateFrame(frame);
        if (!string.Equals(RequiredString(frame, "origin"), "approved_frame", StringComparison.Ordinal))
            throw Invalid("manifest.shot_contract", "A dispatchable frame must be approved.");
    }

    private static void ValidateFrame(JsonObject frame)
    {
        EnsureExactProperties(
            frame,
            ["mediaRef", "assetVersion", "contentHash", "mediaType", "origin"],
            "manifest.shot_contract");
        _ = RequiredString(frame, "mediaRef");
        _ = RequiredString(frame, "assetVersion");
        if (!Sha256Pattern.IsMatch(RequiredString(frame, "contentHash")) ||
            !string.Equals(RequiredString(frame, "mediaType"), "image", StringComparison.Ordinal) ||
            RequiredString(frame, "origin") is not ("approved_frame" or "background_placeholder"))
        {
            throw Invalid("manifest.shot_contract", "A frame reference is invalid.");
        }
    }

    private static void ValidateOptionalMediaPin(JsonNode? node)
    {
        if (node is null) return;
        ValidateMediaPin(
            node as JsonObject
                ?? throw Invalid("manifest.shot_contract", "A media pin must be an object or null."),
            ["video", "audio", "image", "animation", "subtitle"]);
    }

    private static void ValidateMediaPin(JsonObject pin, string[] allowedMediaTypes)
    {
        EnsureExactProperties(
            pin,
            ["mediaRef", "assetVersion", "contentHash", "mediaType"],
            "manifest.shot_contract");
        _ = RequiredString(pin, "mediaRef");
        _ = RequiredString(pin, "assetVersion");
        if (!Sha256Pattern.IsMatch(RequiredString(pin, "contentHash")) ||
            !allowedMediaTypes.Contains(RequiredString(pin, "mediaType")))
        {
            throw Invalid("manifest.shot_contract", "A media pin is invalid.");
        }
    }

    private static GenerationBatchFramePin? MapFrame(JsonNode? node)
    {
        if (node is null) return null;
        JsonObject frame = node.AsObject();
        return new GenerationBatchFramePin(
            RequiredString(frame, "mediaRef"),
            RequiredString(frame, "assetVersion"),
            RequiredString(frame, "contentHash"),
            RequiredString(frame, "mediaType"),
            RequiredString(frame, "origin"));
    }

    private static GenerationBatchMediaPin? MapMediaPin(JsonNode? node)
    {
        if (node is null) return null;
        JsonObject pin = node.AsObject();
        return new GenerationBatchMediaPin(
            RequiredString(pin, "mediaRef"),
            RequiredString(pin, "assetVersion"),
            RequiredString(pin, "contentHash"),
            RequiredString(pin, "mediaType"));
    }

    private static JsonObject RequiredObject(JsonObject value, string propertyName, string code) =>
        value[propertyName] as JsonObject
        ?? throw Invalid(code, $"The property '{propertyName}' must be an object.");

    private static JsonArray RequiredArray(JsonObject value, string propertyName, string code) =>
        value[propertyName] as JsonArray
        ?? throw Invalid(code, $"The property '{propertyName}' must be an array.");

    private static void EnsureStringArray(JsonObject value, string propertyName, bool allowEmpty)
    {
        JsonArray array = RequiredArray(value, propertyName, "manifest.shot_contract");
        if ((!allowEmpty && array.Count == 0) ||
            array.Any(node => node is not JsonValue item ||
                !item.TryGetValue(out string? text) ||
                string.IsNullOrWhiteSpace(text)))
        {
            throw Invalid("manifest.shot_contract", $"The array '{propertyName}' is invalid.");
        }
    }

    private static void EnsureNullableString(JsonObject value, string propertyName)
    {
        JsonNode? node = value[propertyName];
        if (node is null) return;
        if (node is not JsonValue item ||
            !item.TryGetValue(out string? text) ||
            string.IsNullOrWhiteSpace(text))
        {
            throw Invalid("manifest.shot_contract", $"The property '{propertyName}' must be a string or null.");
        }
    }

    private static void EnsureExactProperties(JsonObject value, string[] expected, string code)
    {
        string[] actual = value.Select(property => property.Key).Order(StringComparer.Ordinal).ToArray();
        string[] orderedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(orderedExpected, StringComparer.Ordinal))
            throw Invalid(code, "An object does not match the shot manifest contract.");
    }

    private static void EnsureAllowedProperties(JsonObject value, string[] allowed, string code)
    {
        HashSet<string> allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        if (value.Any(property => !allowedSet.Contains(property.Key)))
            throw Invalid(code, "An object contains an unsupported property.");
    }

    private static string RequiredString(JsonObject value, string propertyName)
    {
        if (value[propertyName] is JsonValue item &&
            item.TryGetValue(out string? result) &&
            !string.IsNullOrWhiteSpace(result))
        {
            return result;
        }

        throw Invalid("manifest.required", $"The shot manifest property '{propertyName}' is required.");
    }

    private static bool RequiredBoolean(JsonObject value, string propertyName)
    {
        if (value[propertyName] is JsonValue item && item.TryGetValue(out bool result))
            return result;
        throw Invalid("manifest.required", $"The shot manifest property '{propertyName}' must be boolean.");
    }

    private static int OptionalPositiveInt32(JsonObject value, string propertyName)
    {
        JsonNode? node = value[propertyName];
        if (node is null) return 0;
        if (node is JsonValue item && item.TryGetValue(out int result) && result > 0)
            return result;
        throw Invalid("manifest.duration", "A shot duration must be null or a positive integer.");
    }

    private static long RequiredNonNegativeInt64(JsonObject value, string propertyName)
    {
        if (value[propertyName] is JsonValue item &&
            item.TryGetValue(out long result) &&
            result >= 0)
        {
            return result;
        }

        throw Invalid("manifest.maximum_cost", "A shot maximum cost must be a non-negative integer.");
    }

    private static byte[] SerializeCanonical(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, node);
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
                foreach (KeyValuePair<string, JsonNode?> property in value.OrderBy(
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
                foreach (JsonNode? item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static string Hash(byte[] content) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static GenerationBatchValidationException Invalid(
        string code,
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new GenerationBatchValidationException(code, message)
            : new GenerationBatchValidationException(code, message + " " + innerException.GetType().Name);
}
