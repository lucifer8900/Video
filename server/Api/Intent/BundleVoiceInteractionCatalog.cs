using System.Globalization;
using System.Text.Json;

namespace Lingmai.RedMist.Api.Intent;

public sealed class BundleVoiceInteractionCatalog : IVoiceInteractionCatalog
{
    private static readonly IReadOnlyDictionary<string, InvalidInputKind> RuleKinds =
        new Dictionary<string, InvalidInputKind>(StringComparer.Ordinal)
        {
            ["abuse"] = InvalidInputKind.Abuse,
            ["irrelevant"] = InvalidInputKind.Irrelevant,
            ["too_long"] = InvalidInputKind.TooLong,
            ["silence"] = InvalidInputKind.Silence,
            ["low_confidence"] = InvalidInputKind.LowConfidence,
        };

    private static readonly HashSet<string> SevereOperations = new(StringComparer.Ordinal)
    {
        "attack",
        "permanent_relationship_break",
        "quest_failure",
    };

    private readonly IReadOnlyDictionary<string, NodeVoicePolicy> _byNode;

    public BundleVoiceInteractionCatalog(IntentOptions options, IIntentCatalog intentCatalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(intentCatalog);
        if (string.IsNullOrWhiteSpace(options.StoryBundlePath))
            throw new InvalidOperationException("Intent:StoryBundlePath must not be empty.");

        string path = StoryBundlePathResolver.Resolve(options.StoryBundlePath);
        _byNode = Load(path, intentCatalog);
    }

    internal BundleVoiceInteractionCatalog(
        IReadOnlyDictionary<string, NodeVoicePolicy> byNode)
    {
        _byNode = byNode ?? throw new ArgumentNullException(nameof(byNode));
    }

    public bool TryGetNodePolicy(string nodeId, out NodeVoicePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            policy = null!;
            return false;
        }
        return _byNode.TryGetValue(nodeId, out policy!);
    }

    private static IReadOnlyDictionary<string, NodeVoicePolicy> Load(
        string path,
        IIntentCatalog intentCatalog)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Voice interaction catalog could not read the configured story bundle.", exception);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            });
            JsonElement root = RequireObject(document.RootElement, "/");
            RequireExactString(root, "schemaVersion", "/schemaVersion", "1.0.0");
            RequireExactString(root, "approvalStatus", "/approvalStatus", "approved");
            Dictionary<string, NpcResponsePolicy> responses = ParseResponses(root);
            Dictionary<string, string> intentResponses = ParseIntentResponses(root, responses);
            return ParseNodes(root, responses, intentResponses, intentCatalog);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Voice interaction catalog is not valid JSON.", exception);
        }
    }

    private static Dictionary<string, NpcResponsePolicy> ParseResponses(JsonElement root)
    {
        JsonElement array = RequireArray(root, "npcResponses", "/npcResponses");
        var result = new Dictionary<string, NpcResponsePolicy>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = "/npcResponses/" + index.ToString(CultureInfo.InvariantCulture);
            JsonElement response = RequireObject(element, path);
            RequireExactString(response, "schemaVersion", path + "/schemaVersion", "1.0.0");
            RequireExactString(response, "approvalStatus", path + "/approvalStatus", "approved");
            string id = RequireNonEmptyString(response, "id", path + "/id");
            if (result.ContainsKey(id)) throw Contract(path + "/id", "duplicates response '" + id + "'.");

            JsonElement effects = RequireArray(response, "effects", path + "/effects");
            var parsedEffects = new List<NpcEffectPolicy>();
            int effectIndex = 0;
            foreach (JsonElement effectElement in effects.EnumerateArray())
            {
                string effectPath = path + "/effects/" + effectIndex.ToString(CultureInfo.InvariantCulture);
                JsonElement effect = RequireObject(effectElement, effectPath);
                string operation = RequireNonEmptyString(effect, "op", effectPath + "/op");
                NpcConfirmationPolicy? confirmation = null;
                if (effect.TryGetProperty("confirmationRule", out JsonElement ruleElement))
                    confirmation = ParseConfirmation(RequireObject(ruleElement, effectPath + "/confirmationRule"), effectPath + "/confirmationRule");
                if (SevereOperations.Contains(operation) && confirmation is null)
                    throw Contract(effectPath + "/confirmationRule", "is required for a severe consequence.");
                parsedEffects.Add(new NpcEffectPolicy(operation, confirmation));
                effectIndex++;
            }
            result.Add(id, new NpcResponsePolicy(id, parsedEffects));
            index++;
        }
        return result;
    }

    private static NpcConfirmationPolicy ParseConfirmation(JsonElement rule, string path)
    {
        string mode = RequireNonEmptyString(rule, "mode", path + "/mode");
        if (string.Equals(mode, "high_confidence", StringComparison.Ordinal))
        {
            return NpcConfirmationPolicy.HighConfidence(
                RequireFiniteUnitNumber(rule, "minimumConfidence", path + "/minimumConfidence"));
        }
        if (string.Equals(mode, "npc_second_confirmation", StringComparison.Ordinal))
        {
            return NpcConfirmationPolicy.SecondConfirmation(
                RequireNonEmptyString(rule, "npcResponseRef", path + "/npcResponseRef"));
        }
        throw Contract(path + "/mode", "contains unsupported confirmation mode '" + mode + "'.");
    }

    private static Dictionary<string, string> ParseIntentResponses(
        JsonElement root,
        IReadOnlyDictionary<string, NpcResponsePolicy> responses)
    {
        JsonElement array = RequireArray(root, "voiceIntents", "/voiceIntents");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = "/voiceIntents/" + index.ToString(CultureInfo.InvariantCulture);
            JsonElement intent = RequireObject(element, path);
            RequireExactString(intent, "schemaVersion", path + "/schemaVersion", "1.0.0");
            RequireExactString(intent, "approvalStatus", path + "/approvalStatus", "approved");
            string id = RequireNonEmptyString(intent, "id", path + "/id");
            string responseId = RequireNonEmptyString(intent, "npcResponseRef", path + "/npcResponseRef");
            if (!responses.ContainsKey(responseId))
                throw Contract(path + "/npcResponseRef", "references unknown response '" + responseId + "'.");
            if (!result.TryAdd(id, responseId))
                throw Contract(path + "/id", "duplicates intent '" + id + "'.");
            index++;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, NodeVoicePolicy> ParseNodes(
        JsonElement root,
        IReadOnlyDictionary<string, NpcResponsePolicy> responses,
        IReadOnlyDictionary<string, string> intentResponses,
        IIntentCatalog intentCatalog)
    {
        JsonElement array = RequireArray(root, "sceneNodes", "/sceneNodes");
        var result = new Dictionary<string, NodeVoicePolicy>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = "/sceneNodes/" + index.ToString(CultureInfo.InvariantCulture);
            JsonElement node = RequireObject(element, path);
            string nodeId = RequireNonEmptyString(node, "id", path + "/id");
            if (!intentCatalog.TryGetAllowedIntents(nodeId, out IReadOnlyList<IntentCandidate> allowedIntents))
                throw Contract(path + "/id", "is absent from the scene intent catalog.");

            var nodeResponseIds = new HashSet<string>(StringComparer.Ordinal);
            if (node.TryGetProperty("npcResponseRefs", out JsonElement nodeResponsesElement))
            {
                JsonElement nodeResponseArray = RequireArray(nodeResponsesElement, path + "/npcResponseRefs");
                int responseIndex = 0;
                foreach (JsonElement responseElement in nodeResponseArray.EnumerateArray())
                {
                    string responsePath = path + "/npcResponseRefs/" + responseIndex.ToString(CultureInfo.InvariantCulture);
                    string responseId = RequireStringValue(responseElement, responsePath);
                    if (!nodeResponseIds.Add(responseId)) throw Contract(responsePath, "duplicates '" + responseId + "'.");
                    if (!responses.ContainsKey(responseId)) throw Contract(responsePath, "references unknown response '" + responseId + "'.");
                    responseIndex++;
                }
            }

            var nodeIntentResponses = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonElement voiceRefs = RequireArray(node, "voiceIntentRefs", path + "/voiceIntentRefs");
            int intentIndex = 0;
            foreach (JsonElement intentElement in voiceRefs.EnumerateArray())
            {
                string intentPath = path + "/voiceIntentRefs/" + intentIndex.ToString(CultureInfo.InvariantCulture);
                string intentId = RequireStringValue(intentElement, intentPath);
                if (!intentResponses.TryGetValue(intentId, out string? responseId))
                    throw Contract(intentPath, "references unknown intent '" + intentId + "'.");
                if (!nodeResponseIds.Contains(responseId))
                    throw Contract(intentPath, "references a response outside npcResponseRefs.");
                if (!nodeIntentResponses.TryAdd(intentId, responseId))
                    throw Contract(intentPath, "duplicates '" + intentId + "'.");
                intentIndex++;
            }

            var invalidResponses = new Dictionary<InvalidInputKind, string>();
            if (node.TryGetProperty("invalid_input_rules", out JsonElement rulesElement))
            {
                JsonElement rules = RequireObject(rulesElement, path + "/invalid_input_rules");
                foreach (JsonProperty property in rules.EnumerateObject())
                {
                    string rulePath = path + "/invalid_input_rules/" + Escape(property.Name);
                    if (!RuleKinds.TryGetValue(property.Name, out InvalidInputKind kind))
                        throw Contract(rulePath, "uses an unsupported invalid-input kind.");
                    JsonElement rule = RequireObject(property.Value, rulePath);
                    string responseId = RequireNonEmptyString(rule, "npcResponseRef", rulePath + "/npcResponseRef");
                    if (!nodeResponseIds.Contains(responseId))
                        throw Contract(rulePath + "/npcResponseRef", "references a response outside npcResponseRefs.");
                    invalidResponses.Add(kind, responseId);
                }
                foreach (InvalidInputKind required in RuleKinds.Values)
                {
                    if (!invalidResponses.ContainsKey(required))
                        throw Contract(path + "/invalid_input_rules", "does not define all five required reactions.");
                }
            }
            else if (allowedIntents.Count > 0)
            {
                throw Contract(path + "/invalid_input_rules", "is required for a voice-enabled node.");
            }

            var nodeResponses = nodeResponseIds.ToDictionary(
                id => id,
                id => responses[id],
                StringComparer.Ordinal);
            ValidateConfirmationRefs(nodeResponses, path);
            if (!result.TryAdd(
                    nodeId,
                    new NodeVoicePolicy(allowedIntents, nodeIntentResponses, invalidResponses, nodeResponses)))
            {
                throw Contract(path + "/id", "duplicates node '" + nodeId + "'.");
            }
            index++;
        }
        return result;
    }

    private static void ValidateConfirmationRefs(
        IReadOnlyDictionary<string, NpcResponsePolicy> nodeResponses,
        string nodePath)
    {
        foreach (NpcResponsePolicy response in nodeResponses.Values)
        {
            foreach (NpcEffectPolicy effect in response.Effects)
            {
                if (effect.Confirmation?.Mode != NpcConfirmationMode.NpcSecondConfirmation) continue;
                string? id = effect.Confirmation.NpcResponseRef;
                if (id is null || !nodeResponses.ContainsKey(id))
                    throw Contract(nodePath + "/npcResponseRefs", "does not allowlist second-confirmation response '" + id + "'.");
            }
        }
    }

    private static JsonElement RequireArray(JsonElement owner, string property, string path) =>
        RequireArray(RequireProperty(owner, property, path), path);

    private static JsonElement RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Contract(path, "must be an array.");
        return value;
    }

    private static JsonElement RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Contract(path, "must be an object.");
        return value;
    }

    private static JsonElement RequireProperty(JsonElement owner, string property, string path)
    {
        if (!owner.TryGetProperty(property, out JsonElement value)) throw Contract(path, "is required.");
        return value;
    }

    private static string RequireNonEmptyString(JsonElement owner, string property, string path) =>
        RequireStringValue(RequireProperty(owner, property, path), path);

    private static string RequireStringValue(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Contract(path, "must be a non-empty string.");
        return value.GetString()!;
    }

    private static void RequireExactString(
        JsonElement owner,
        string property,
        string path,
        string expected)
    {
        string value = RequireNonEmptyString(owner, property, path);
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            throw Contract(path, "must equal '" + expected + "'.");
    }

    private static double RequireFiniteUnitNumber(JsonElement owner, string property, string path)
    {
        JsonElement value = RequireProperty(owner, property, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
            !double.IsFinite(number) || number is < 0d or > 1d)
        {
            throw Contract(path, "must be a finite number from zero through one.");
        }
        return number;
    }

    private static InvalidOperationException Contract(string path, string message) =>
        new("Voice interaction catalog " + path + " " + message);

    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
}
