using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceEvaluator;

public sealed class VoiceEvaluationDatasetLoader
{
    public const int RequiredSampleCount = 200;
    public const int MaximumAcceptedTranscriptCharacters = 4096;

    private static readonly HashSet<string> ExpectedOutcomes = new(StringComparer.Ordinal)
    {
        "matched",
        "irrelevant",
        "abuse",
        "too_long",
        "silence",
        "low_confidence",
    };

    private static readonly JsonSerializerOptions InputOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public LoadedVoiceEvaluationDataset Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The dataset path must not be empty.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The voice evaluation dataset was not found.", fullPath);

        byte[] bytes = File.ReadAllBytes(fullPath);
        VoiceEvaluationDataset dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<VoiceEvaluationDataset>(bytes, InputOptions)
                ?? throw new InvalidDataException("The dataset root must be an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The voice evaluation dataset is not valid strict JSON.", exception);
        }

        Validate(dataset);
        string hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new LoadedVoiceEvaluationDataset(dataset, hash);
    }

    private static void Validate(VoiceEvaluationDataset dataset)
    {
        RequireExact(dataset.SchemaVersion, "1.0.0", "/schemaVersion");
        RequireIdentifier(dataset.DatasetId, "/datasetId");
        RequireExact(dataset.Language, "zh-CN", "/language");
        RequireExact(dataset.Scope, "synthetic_text_regression", "/scope");
        if (!dataset.TestOnly) Contract("/testOnly", "must be true.");
        if (!dataset.DoNotShip) Contract("/doNotShip", "must be true.");
        if (dataset.StoryCharacterLimit is < 1 or > MaximumAcceptedTranscriptCharacters)
            Contract("/storyCharacterLimit", "must be from 1 through 4096.");
        if (dataset.Nodes is null || dataset.Nodes.Count == 0)
            Contract("/nodes", "must contain at least one node.");
        if (dataset.Samples is null || dataset.Samples.Count != RequiredSampleCount)
            Contract("/samples", $"must contain exactly {RequiredSampleCount} samples.");
        if (dataset.SevereIntentIds is null || dataset.SevereIntentIds.Count == 0)
            Contract("/severeIntentIds", "must contain at least one severe sentinel intent.");

        var nodes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allIntentIds = new HashSet<string>(StringComparer.Ordinal);
        for (var nodeIndex = 0; nodeIndex < dataset.Nodes.Count; nodeIndex++)
        {
            VoiceEvaluationNode node = dataset.Nodes[nodeIndex]
                ?? throw ContractException($"/nodes/{nodeIndex}", "must be an object.");
            RequireIdentifier(node.NodeId, $"/nodes/{nodeIndex}/nodeId");
            if (!nodes.TryAdd(node.NodeId, new HashSet<string>(StringComparer.Ordinal)))
                Contract($"/nodes/{nodeIndex}/nodeId", "must be unique.");
            if (node.Intents is null || node.Intents.Count == 0)
                Contract($"/nodes/{nodeIndex}/intents", "must contain at least one intent.");

            for (var intentIndex = 0; intentIndex < node.Intents.Count; intentIndex++)
            {
                VoiceEvaluationIntent intent = node.Intents[intentIndex]
                    ?? throw ContractException($"/nodes/{nodeIndex}/intents/{intentIndex}", "must be an object.");
                string pointer = $"/nodes/{nodeIndex}/intents/{intentIndex}";
                RequireIdentifier(intent.Id, pointer + "/id");
                if (ExpectedOutcomes.Contains(intent.Id))
                    Contract(pointer + "/id", "must not use a reserved outcome ID.");
                if (!nodes[node.NodeId].Add(intent.Id))
                    Contract(pointer + "/id", "must be unique within its node.");
                allIntentIds.Add(intent.Id);
                if (!double.IsFinite(intent.MinimumConfidence) || intent.MinimumConfidence is < 0d or > 1d)
                    Contract(pointer + "/minimumConfidence", "must be from 0 through 1.");
                if (intent.KeywordPhrases is null || intent.KeywordPhrases.Count == 0)
                    Contract(pointer + "/keywordPhrases", "must contain at least one phrase.");
                var phrases = new HashSet<string>(StringComparer.Ordinal);
                for (var phraseIndex = 0; phraseIndex < intent.KeywordPhrases.Count; phraseIndex++)
                {
                    string phrase = intent.KeywordPhrases[phraseIndex];
                    if (string.IsNullOrWhiteSpace(phrase))
                        Contract(pointer + $"/keywordPhrases/{phraseIndex}", "must be non-empty.");
                    if (!phrases.Add(phrase.Normalize(NormalizationForm.FormKC)))
                        Contract(pointer + $"/keywordPhrases/{phraseIndex}", "must be unique after normalization.");
                }
            }
        }

        var severeIds = new HashSet<string>(StringComparer.Ordinal);
        for (var severeIndex = 0; severeIndex < dataset.SevereIntentIds.Count; severeIndex++)
        {
            string id = dataset.SevereIntentIds[severeIndex];
            RequireIdentifier(id, $"/severeIntentIds/{severeIndex}");
            if (!severeIds.Add(id)) Contract($"/severeIntentIds/{severeIndex}", "must be unique.");
            if (!allIntentIds.Contains(id))
                Contract($"/severeIntentIds/{severeIndex}", "must reference a declared intent.");
        }

        var sampleIds = new HashSet<string>(StringComparer.Ordinal);
        var normalizedSamples = new HashSet<string>(StringComparer.Ordinal);
        var matchedCount = 0;
        var nonSevereCount = 0;
        for (var sampleIndex = 0; sampleIndex < dataset.Samples.Count; sampleIndex++)
        {
            VoiceEvaluationSample sample = dataset.Samples[sampleIndex]
                ?? throw ContractException($"/samples/{sampleIndex}", "must be an object.");
            string pointer = $"/samples/{sampleIndex}";
            RequireIdentifier(sample.Id, pointer + "/id");
            if (!sampleIds.Add(sample.Id)) Contract(pointer + "/id", "must be unique.");
            RequireIdentifier(sample.NodeId, pointer + "/nodeId");
            if (!nodes.TryGetValue(sample.NodeId, out HashSet<string>? nodeIntents))
                Contract(pointer + "/nodeId", "must reference a declared node.");
            if (!ExpectedOutcomes.Contains(sample.ExpectedOutcome))
                Contract(pointer + "/expectedOutcome", "contains an unsupported outcome.");
            if (sample.Transcript is null) Contract(pointer + "/transcript", "must not be null.");
            if (sample.Tags is null) Contract(pointer + "/tags", "must be an array.");

            string normalized = sample.Transcript.Normalize(NormalizationForm.FormKC).Trim();
            if (sample.ExpectedOutcome == "silence")
            {
                if (normalized.Length != 0) Contract(pointer + "/transcript", "must be blank for silence.");
            }
            else
            {
                if (normalized.Length == 0) Contract(pointer + "/transcript", "must not be blank.");
                if (!ContainsCjk(normalized)) Contract(pointer + "/transcript", "must contain Chinese text.");
            }

            if (sample.ExpectedOutcome == "too_long")
            {
                if (normalized.Length <= dataset.StoryCharacterLimit ||
                    normalized.Length > MaximumAcceptedTranscriptCharacters)
                {
                    Contract(pointer + "/transcript", "must be longer than the story limit and at most 4096 characters.");
                }
            }
            else if (normalized.Length > dataset.StoryCharacterLimit)
            {
                Contract(pointer + "/transcript", "must not exceed the story character limit.");
            }

            string dedupeText = sample.ExpectedOutcome == "silence"
                ? sample.Transcript
                : normalized;
            string dedupeKey = sample.NodeId + "\u001f" + dedupeText + "\u001f" + sample.ExpectedOutcome;
            if (!normalizedSamples.Add(dedupeKey))
                Contract(pointer + "/transcript", "duplicates another normalized sample in the same node and class.");

            if (sample.ExpectedOutcome == "matched")
            {
                matchedCount++;
                if (string.IsNullOrWhiteSpace(sample.ExpectedIntentId))
                    Contract(pointer + "/expectedIntentId", "is required for a matched sample.");
                if (!nodeIntents.Contains(sample.ExpectedIntentId))
                    Contract(pointer + "/expectedIntentId", "must reference an intent allowed by the sample node.");
                if (!severeIds.Contains(sample.ExpectedIntentId)) nonSevereCount++;
            }
            else
            {
                if (sample.ExpectedIntentId is not null)
                    Contract(pointer + "/expectedIntentId", "must be null for a non-matched sample.");
                nonSevereCount++;
            }
        }

        if (matchedCount == 0) Contract("/samples", "must contain at least one matched sample.");
        if (nonSevereCount == 0) Contract("/samples", "must contain at least one non-severe sample.");
    }

    private static bool ContainsCjk(string value) => value.Any(character =>
        character is >= '\u3400' and <= '\u4dbf' or
        >= '\u4e00' and <= '\u9fff' or
        >= '\uf900' and <= '\ufaff');

    private static void RequireIdentifier(string? value, string pointer)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsWhiteSpace))
            Contract(pointer, "must be a non-empty identifier without whitespace and at most 128 characters.");
    }

    private static void RequireExact(string? actual, string expected, string pointer)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            Contract(pointer, $"must equal '{expected}'.");
    }

    [DoesNotReturn]
    private static void Contract(string pointer, string message) =>
        throw ContractException(pointer, message);

    private static InvalidDataException ContractException(string pointer, string message) =>
        new($"{pointer} {message}");
}
