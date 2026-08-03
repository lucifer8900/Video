using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lingmai.RedMist.Api.Intent;

public interface IIntentCatalog
{
    bool TryGetAllowedIntents(
        string nodeId,
        out IReadOnlyList<IntentCandidate> allowedIntents);
}

public sealed class BundleIntentCatalog : IIntentCatalog
{
    private static readonly HashSet<string> ReservedIntentIds = new(StringComparer.Ordinal)
    {
        "irrelevant",
        "low_confidence",
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyList<IntentCandidate>> _byNode;

    public BundleIntentCatalog(IntentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.StoryBundlePath))
            throw new InvalidOperationException("Intent:StoryBundlePath must not be empty.");

        string path = StoryBundlePathResolver.Resolve(options.StoryBundlePath);
        _byNode = Load(path);
    }

    internal BundleIntentCatalog(
        IReadOnlyDictionary<string, IReadOnlyList<IntentCandidate>> byNode)
    {
        _byNode = byNode ?? throw new ArgumentNullException(nameof(byNode));
    }

    public bool TryGetAllowedIntents(
        string nodeId,
        out IReadOnlyList<IntentCandidate> allowedIntents)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            allowedIntents = Array.Empty<IntentCandidate>();
            return false;
        }

        return _byNode.TryGetValue(nodeId, out allowedIntents!);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IntentCandidate>> Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The configured story bundle could not be read.", exception);
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
            RequireString(root, "schemaVersion", "/schemaVersion", "1.0.0");
            RequireString(root, "approvalStatus", "/approvalStatus", "approved");

            JsonElement localizations = RequireObjectProperty(root, "localizations", "/localizations");
            JsonElement chinese = RequireObjectProperty(localizations, "zh-CN", "/localizations/zh-CN");
            var localized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in chinese.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    throw Contract("/localizations/zh-CN/" + Escape(property.Name), "must be a non-empty string.");
                }

                if (!localized.TryAdd(property.Name, property.Value.GetString()!))
                    throw Contract("/localizations/zh-CN/" + Escape(property.Name), "is duplicated.");
            }

            Dictionary<string, IntentCandidate> intents = ParseIntents(root, localized);
            return ParseNodes(root, intents);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The configured story bundle is not valid JSON.", exception);
        }
    }

    private static Dictionary<string, IntentCandidate> ParseIntents(
        JsonElement root,
        IReadOnlyDictionary<string, string> localized)
    {
        JsonElement array = RequireArrayProperty(root, "voiceIntents", "/voiceIntents");
        var result = new Dictionary<string, IntentCandidate>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement value in array.EnumerateArray())
        {
            string path = "/voiceIntents/" + index.ToString(CultureInfo.InvariantCulture);
            JsonElement item = RequireObject(value, path);
            RequireString(item, "schemaVersion", path + "/schemaVersion", "1.0.0");
            RequireString(item, "approvalStatus", path + "/approvalStatus", "approved");
            string id = RequireNonEmptyString(item, "id", path + "/id");
            if (ReservedIntentIds.Contains(id))
                throw Contract(path + "/id", "uses a reserved classifier outcome.");
            if (result.ContainsKey(id))
                throw Contract(path + "/id", "duplicates intent '" + id + "'.");

            ResolveLocalization(item, "displayKey", localized, path + "/displayKey");
            ResolveLocalization(item, "toneKey", localized, path + "/toneKey");
            double threshold = RequireFiniteUnitNumber(item, "minimumConfidence", path + "/minimumConfidence");
            JsonElement topics = RequireArrayProperty(item, "topicKeys", path + "/topicKeys");
            var phrases = new List<string>();
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            int topicIndex = 0;
            foreach (JsonElement topic in topics.EnumerateArray())
            {
                string topicPath = path + "/topicKeys/" + topicIndex.ToString(CultureInfo.InvariantCulture);
                if (topic.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(topic.GetString()))
                    throw Contract(topicPath, "must be a non-empty localization key.");
                if (!localized.TryGetValue(topic.GetString()!, out string? phrase) || string.IsNullOrWhiteSpace(phrase))
                    throw Contract(topicPath, "references a missing zh-CN localization.");
                string cleaned = phrase.Trim().Normalize(NormalizationForm.FormKC);
                if (normalized.Add(cleaned)) phrases.Add(cleaned);
                topicIndex++;
            }

            if (phrases.Count == 0)
                throw Contract(path + "/topicKeys", "must resolve at least one local fallback phrase.");
            result.Add(id, new IntentCandidate(id, phrases, threshold));
            index++;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IntentCandidate>> ParseNodes(
        JsonElement root,
        IReadOnlyDictionary<string, IntentCandidate> intents)
    {
        JsonElement array = RequireArrayProperty(root, "sceneNodes", "/sceneNodes");
        var result = new Dictionary<string, IReadOnlyList<IntentCandidate>>(StringComparer.Ordinal);
        int nodeIndex = 0;
        foreach (JsonElement value in array.EnumerateArray())
        {
            string path = "/sceneNodes/" + nodeIndex.ToString(CultureInfo.InvariantCulture);
            JsonElement node = RequireObject(value, path);
            string id = RequireNonEmptyString(node, "id", path + "/id");
            if (result.ContainsKey(id)) throw Contract(path + "/id", "duplicates node '" + id + "'.");

            JsonElement refs = RequireArrayProperty(node, "voiceIntentRefs", path + "/voiceIntentRefs");
            var allowed = new List<IntentCandidate>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            int refIndex = 0;
            foreach (JsonElement reference in refs.EnumerateArray())
            {
                string refPath = path + "/voiceIntentRefs/" + refIndex.ToString(CultureInfo.InvariantCulture);
                if (reference.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reference.GetString()))
                    throw Contract(refPath, "must be a non-empty intent id.");
                string intentId = reference.GetString()!;
                if (!unique.Add(intentId)) throw Contract(refPath, "duplicates '" + intentId + "'.");
                if (!intents.TryGetValue(intentId, out IntentCandidate? candidate))
                    throw Contract(refPath, "references unknown intent '" + intentId + "'.");
                allowed.Add(candidate);
                refIndex++;
            }

            result.Add(id, allowed);
            nodeIndex++;
        }

        return result;
    }

    private static string ResolveLocalization(
        JsonElement owner,
        string property,
        IReadOnlyDictionary<string, string> localized,
        string path)
    {
        string key = RequireNonEmptyString(owner, property, path);
        if (!localized.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            throw Contract(path, "references a missing zh-CN localization.");
        return value;
    }

    private static JsonElement RequireObjectProperty(JsonElement owner, string property, string path) =>
        RequireObject(RequireProperty(owner, property, path), path);

    private static JsonElement RequireArrayProperty(JsonElement owner, string property, string path)
    {
        JsonElement value = RequireProperty(owner, property, path);
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

    private static string RequireNonEmptyString(JsonElement owner, string property, string path)
    {
        JsonElement value = RequireProperty(owner, property, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Contract(path, "must be a non-empty string.");
        return value.GetString()!;
    }

    private static void RequireString(
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
            throw Contract(path, "must be a finite number from 0 through 1.");
        }

        return number;
    }

    private static InvalidOperationException Contract(string path, string message) =>
        new("Intent catalog " + path + " " + message);

    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
}
