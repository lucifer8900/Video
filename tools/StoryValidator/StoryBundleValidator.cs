using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace StoryValidator;

public sealed record StoryDiagnostic(string Code, string Path, string Message);

public sealed record StoryValidationResult(
    bool Valid,
    IReadOnlyList<StoryDiagnostic> Diagnostics);

public sealed class StoryBundleValidator
{
    private static readonly string[] EntityCollections =
    [
        "storyFacts",
        "characterDefinitions",
        "characterStates",
        "questDefinitions",
        "sceneNodes",
        "voiceIntents",
        "npcResponses",
        "combatantStates",
        "encounterDefinitions",
        "combatResolutions",
        "mediaAssets",
        "generationJobs",
        "saveGames",
    ];

    private static readonly IReadOnlyDictionary<string, ReferenceRule> ReferenceRules =
        new Dictionary<string, ReferenceRule>(StringComparer.Ordinal)
        {
            ["storyBundleId"] = new("storyBundles"),
            ["factRef"] = new("storyFacts"),
            ["factRefs"] = new("storyFacts"),
            ["characterId"] = new("characterDefinitions"),
            ["characterStateRef"] = new("characterStates"),
            ["characterStateRefs"] = new("characterStates"),
            ["questId"] = new("questDefinitions"),
            ["entryNodeId"] = new("sceneNodes"),
            ["currentNodeId"] = new("sceneNodes"),
            ["nextNodeId"] = new("sceneNodes"),
            ["offlineFallbackNodeId"] = new("sceneNodes"),
            ["voiceIntentRef"] = new("voiceIntents"),
            ["voiceIntentRefs"] = new("voiceIntents"),
            ["npcResponseRef"] = new("npcResponses"),
            ["npcResponseRefs"] = new("npcResponses"),
            ["fallbackNpcResponseRef"] = new("npcResponses"),
            ["combatantRef"] = new("combatantStates"),
            ["combatantRefs"] = new("combatantStates"),
            ["combatantIds"] = new("combatantStates"),
            ["enemyIds"] = new("combatantStates"),
            ["encounterId"] = new("encounterDefinitions"),
            ["generationJobRef"] = new("generationJobs"),
            ["mediaRef"] = new("mediaAssets", IsMedia: true),
            ["mediaRefs"] = new("mediaAssets", IsMedia: true),
            ["primaryMediaRef"] = new("mediaAssets", IsMedia: true),
            ["fallbackMediaRef"] = new("mediaAssets", IsMedia: true),
            ["backgroundMediaRef"] = new("mediaAssets", IsMedia: true),
            ["portraitMediaRef"] = new("mediaAssets", IsMedia: true),
            ["introMediaRef"] = new("mediaAssets", IsMedia: true),
            ["lipSyncMediaRef"] = new("mediaAssets", IsMedia: true),
            ["audioMediaRef"] = new("mediaAssets", IsMedia: true),
            ["pendingMediaRef"] = new("mediaAssets", IsMedia: true),
            ["watchedMediaRefs"] = new("mediaAssets", IsMedia: true),
            ["resultMediaRef"] = new("mediaAssets", IsMedia: true),
            ["dependencyRefs"] = new("mediaAssets", IsMedia: true),
        };

    private static readonly Regex JsonArrayInMessage =
        new(@"\[[^\]]*\]", RegexOptions.Compiled);

    public StoryValidationResult Validate(string bundlePath, string schemaDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDirectory);

        var json = File.ReadAllText(bundlePath);
        using var document = JsonDocument.Parse(json);
        var schemaCatalog = LoadSchemas(schemaDirectory);
        var evaluation = schemaCatalog.BundleSchema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });

        if (!evaluation.IsValid)
        {
            return CreateResult(SchemaDiagnostics(evaluation));
        }

        var bundle = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("The story bundle root must be a JSON object.");
        var diagnostics = new List<StoryDiagnostic>();
        var registries = BuildRegistries(bundle);

        diagnostics.AddRange(ContentHashDiagnostics(bundle));
        diagnostics.AddRange(LocalizationDiagnostics(bundle));
        diagnostics.AddRange(DuplicateIdDiagnostics(bundle));
        var referenceDiagnostics = ReferenceDiagnostics(bundle, registries).ToArray();
        diagnostics.AddRange(referenceDiagnostics);
        diagnostics.AddRange(QuestLifecycleDiagnostics(bundle));
        diagnostics.AddRange(MediaFileDiagnostics(bundle, bundlePath));

        var graphIsSafeToAnalyze =
            !diagnostics.Any(IsGraphRegistryDuplicate) &&
            !referenceDiagnostics.Any(IsGraphReferenceDiagnostic);

        if (graphIsSafeToAnalyze)
        {
            diagnostics.AddRange(GraphDiagnostics(bundle));
        }

        return CreateResult(diagnostics);
    }

    private static IEnumerable<StoryDiagnostic> ContentHashDiagnostics(JsonObject bundle)
    {
        // distributionStatus identifies compiler-produced L1 bundles. Older schema fixtures do
        // not carry it and remain backward compatible while all new bundles are hash-enforced.
        if (GetString(bundle["distributionStatus"]) is null ||
            GetString(bundle["contentHash"]) is not { } actualHash)
        {
            yield break;
        }

        var content = bundle.DeepClone().AsObject();
        content.Remove("contentHash");
        var expectedHash = "sha256:" + Convert.ToHexString(
            SHA256.HashData(SerializeCanonical(content))).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            yield return new StoryDiagnostic(
                "content.hash_mismatch",
                "/contentHash",
                $"The content hash is '{actualHash}' but the canonical bundle hash is '{expectedHash}'.");
        }
    }

    private static IEnumerable<StoryDiagnostic> LocalizationDiagnostics(JsonObject bundle)
    {
        if (bundle["localizations"] is not JsonObject localizations ||
            bundle["sceneNodes"] is not JsonArray sceneNodes)
        {
            yield break;
        }

        string[] nodeKeyProperties =
        [
            "displayKey",
            "titleKey",
            "locationKey",
            "speakerKey",
            "maleTextKey",
            "femaleTextKey",
        ];

        for (var nodeIndex = 0; nodeIndex < sceneNodes.Count; nodeIndex++)
        {
            if (sceneNodes[nodeIndex] is not JsonObject node)
            {
                continue;
            }

            foreach (var propertyName in nodeKeyProperties)
            {
                foreach (var diagnostic in MissingLocalizationDiagnostics(
                             localizations,
                             node[propertyName],
                             $"/sceneNodes/{nodeIndex}/{propertyName}"))
                {
                    yield return diagnostic;
                }
            }

            if (node["choices"] is not JsonArray choices)
            {
                continue;
            }

            for (var choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
            {
                if (choices[choiceIndex] is not JsonObject choice)
                {
                    continue;
                }

                foreach (var propertyName in new[] { "labelKey", "hintKey" })
                {
                    foreach (var diagnostic in MissingLocalizationDiagnostics(
                                 localizations,
                                 choice[propertyName],
                                 $"/sceneNodes/{nodeIndex}/choices/{choiceIndex}/{propertyName}"))
                    {
                        yield return diagnostic;
                    }
                }
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> MissingLocalizationDiagnostics(
        JsonObject localizations,
        JsonNode? keyNode,
        string path)
    {
        if (GetString(keyNode) is not { } key)
        {
            yield break;
        }

        foreach (var localization in localizations)
        {
            if (localization.Value is JsonObject entries && !entries.ContainsKey(key))
            {
                yield return new StoryDiagnostic(
                    "localization.key_not_found",
                    path,
                    $"Localization '{localization.Key}' does not define key '{key}'.");
            }
        }
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
                foreach (var property in value.OrderBy(property => property.Key, StringComparer.Ordinal))
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

    private static StoryValidationResult CreateResult(IEnumerable<StoryDiagnostic> diagnostics)
    {
        var ordered = diagnostics
            .Distinct()
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

        return new StoryValidationResult(ordered.Length == 0, ordered);
    }

    private static SchemaCatalog LoadSchemas(string schemaDirectory)
    {
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        var schemas = new List<JsonSchema>();
        JsonSchema? bundleSchema = null;

        foreach (var schemaPath in Directory
                     .EnumerateFiles(schemaDirectory, "*.schema.json")
                     .Order(StringComparer.Ordinal))
        {
            using var schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var schema = JsonSchema.Build(schemaDocument.RootElement.Clone(), buildOptions);
            schemas.Add(schema);

            if (string.Equals(
                    Path.GetFileName(schemaPath),
                    "story-bundle.schema.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                bundleSchema = schema;
            }
        }

        return new SchemaCatalog(
            bundleSchema ?? throw new FileNotFoundException(
                "The story-bundle schema was not found.",
                Path.Combine(schemaDirectory, "story-bundle.schema.json")),
            schemas);
    }

    private static IEnumerable<StoryDiagnostic> SchemaDiagnostics(EvaluationResults evaluation)
    {
        foreach (var result in Flatten(evaluation)
                     .Where(result => !result.IsValid && result.Errors is not null))
        {
            foreach (var error in result.Errors!)
            {
                var basePath = result.InstanceLocation.ToString();
                if (error.Key == "required")
                {
                    foreach (var property in MissingRequiredProperties(error.Value))
                    {
                        yield return new StoryDiagnostic(
                            "schema.required",
                            AppendPointer(basePath, property),
                            error.Value);
                    }

                    continue;
                }

                string keyword = string.IsNullOrEmpty(error.Key) &&
                                 error.Value.Contains("false schema", StringComparison.OrdinalIgnoreCase)
                    ? "additionalProperties"
                    : error.Key;
                yield return new StoryDiagnostic(
                    $"schema.{keyword}",
                    basePath,
                    error.Value);
            }
        }
    }

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults result)
    {
        yield return result;

        if (result.Details is null)
        {
            yield break;
        }

        foreach (var detail in result.Details)
        {
            foreach (var descendant in Flatten(detail))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<string> MissingRequiredProperties(string message)
    {
        var match = JsonArrayInMessage.Match(message);
        if (!match.Success)
        {
            yield break;
        }

        var properties = JsonSerializer.Deserialize<string[]>(match.Value) ?? [];
        foreach (var property in properties)
        {
            yield return property;
        }
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildRegistries(JsonObject bundle)
    {
        var registries = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var collection in EntityCollections)
        {
            registries[collection] = CollectIds(bundle[collection]);
        }

        registries["storyBundles"] = new HashSet<string>(StringComparer.Ordinal);
        if (GetString(bundle["id"]) is { } bundleId)
        {
            registries["storyBundles"].Add(bundleId);
        }

        return registries;
    }

    private static HashSet<string> CollectIds(JsonNode? collection)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Objects(collection))
        {
            if (GetString(item["id"]) is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static IEnumerable<StoryDiagnostic> DuplicateIdDiagnostics(JsonObject bundle)
    {
        var firstPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        return Visit(bundle, "");

        IEnumerable<StoryDiagnostic> Visit(JsonNode? node, string path)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject)
                {
                    var propertyPath = AppendPointer(path, property.Key);
                    if (property.Key == "id" && GetString(property.Value) is { } id)
                    {
                        if (!firstPaths.TryAdd(id, propertyPath))
                        {
                            yield return new StoryDiagnostic(
                                "id.duplicate",
                                propertyPath,
                                $"The id '{id}' is already declared at {firstPaths[id]}.");
                        }
                    }

                    foreach (var diagnostic in Visit(property.Value, propertyPath))
                    {
                        yield return diagnostic;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                for (var index = 0; index < array.Count; index++)
                {
                    foreach (var diagnostic in Visit(
                                 array[index],
                                 AppendPointer(path, index.ToString())))
                    {
                        yield return diagnostic;
                    }
                }
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> ReferenceDiagnostics(
        JsonObject bundle,
        IReadOnlyDictionary<string, HashSet<string>> registries)
    {
        foreach (var diagnostic in Visit(bundle, ""))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in ObjectiveReferenceDiagnostics(bundle))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in DynamicKeyReferenceDiagnostics(bundle, registries))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in RelationshipEffectReferenceDiagnostics(
                     bundle,
                     registries["characterDefinitions"]))
        {
            yield return diagnostic;
        }

        IEnumerable<StoryDiagnostic> Visit(JsonNode? node, string path)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject)
                {
                    var propertyPath = AppendPointer(path, property.Key);
                    if (ReferenceRules.TryGetValue(property.Key, out var rule))
                    {
                        foreach (var diagnostic in ValidateReferenceValue(
                                     property.Value,
                                     propertyPath,
                                     rule,
                                     registries[rule.Registry]))
                        {
                            yield return diagnostic;
                        }
                    }

                    foreach (var diagnostic in Visit(property.Value, propertyPath))
                    {
                        yield return diagnostic;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                for (var index = 0; index < array.Count; index++)
                {
                    foreach (var diagnostic in Visit(
                                 array[index],
                                 AppendPointer(path, index.ToString())))
                    {
                        yield return diagnostic;
                    }
                }
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> ObjectiveReferenceDiagnostics(JsonObject bundle)
    {
        var encounters = IndexById(bundle["encounterDefinitions"]);
        if (bundle["combatResolutions"] is not JsonArray resolutions)
        {
            yield break;
        }

        for (var resolutionIndex = 0; resolutionIndex < resolutions.Count; resolutionIndex++)
        {
            if (resolutions[resolutionIndex] is not JsonObject resolution ||
                GetString(resolution["encounterId"]) is not { } encounterId ||
                !encounters.TryGetValue(encounterId, out var encounter))
            {
                continue;
            }

            var objectiveIds = CollectIds(encounter["objectives"]);
            if (resolution["objectiveResults"] is JsonArray objectiveResults)
            {
                for (var resultIndex = 0; resultIndex < objectiveResults.Count; resultIndex++)
                {
                    if (objectiveResults[resultIndex] is not JsonObject objectiveResult ||
                        GetString(objectiveResult["objectiveId"]) is not { } objectiveId ||
                        objectiveIds.Contains(objectiveId))
                    {
                        continue;
                    }

                    yield return new StoryDiagnostic(
                        "reference.not_found",
                        $"/combatResolutions/{resolutionIndex}/objectiveResults/{resultIndex}/objectiveId",
                        $"Objective '{objectiveId}' is not declared by encounter '{encounterId}'.");
                }
            }

            var seedEncounterId = GetString(resolution["seed"]?["encounterId"]);
            if (seedEncounterId is not null && seedEncounterId != encounterId)
            {
                yield return new StoryDiagnostic(
                    "reference.encounter_mismatch",
                    $"/combatResolutions/{resolutionIndex}/seed/encounterId",
                    $"The seed encounter '{seedEncounterId}' does not match '{encounterId}'.");
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> DynamicKeyReferenceDiagnostics(
        JsonObject bundle,
        IReadOnlyDictionary<string, HashSet<string>> registries)
    {
        if (bundle["characterStates"] is JsonArray characterStates)
        {
            for (var stateIndex = 0; stateIndex < characterStates.Count; stateIndex++)
            {
                if (characterStates[stateIndex] is not JsonObject state ||
                    state["relationships"] is not JsonObject relationships)
                {
                    continue;
                }

                foreach (var relationship in relationships)
                {
                    if (!registries["characterDefinitions"].Contains(relationship.Key))
                    {
                        yield return new StoryDiagnostic(
                            "reference.not_found",
                            AppendPointer($"/characterStates/{stateIndex}/relationships", relationship.Key),
                            $"Character '{relationship.Key}' does not exist.");
                    }
                }
            }
        }

        if (bundle["saveGames"] is not JsonArray saveGames)
        {
            yield break;
        }

        for (var saveIndex = 0; saveIndex < saveGames.Count; saveIndex++)
        {
            if (saveGames[saveIndex] is not JsonObject save ||
                save["stateSnapshot"]?["factFlags"] is not JsonObject factFlags)
            {
                continue;
            }

            foreach (var factFlag in factFlags)
            {
                if (!registries["storyFacts"].Contains(factFlag.Key))
                {
                    yield return new StoryDiagnostic(
                        "reference.not_found",
                        AppendPointer($"/saveGames/{saveIndex}/stateSnapshot/factFlags", factFlag.Key),
                        $"Story fact '{factFlag.Key}' does not exist.");
                }
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> RelationshipEffectReferenceDiagnostics(
        JsonNode? node,
        IReadOnlySet<string> characterIds,
        string path = "")
    {
        if (node is JsonObject jsonObject)
        {
            if (GetString(jsonObject["op"]) == "relationship" &&
                GetString(jsonObject["targetRef"]) is { } targetRef &&
                !characterIds.Contains(targetRef))
            {
                yield return new StoryDiagnostic(
                    "reference.not_found",
                    AppendPointer(path, "targetRef"),
                    $"Character '{targetRef}' does not exist.");
            }

            foreach (var property in jsonObject)
            {
                foreach (var diagnostic in RelationshipEffectReferenceDiagnostics(
                             property.Value,
                             characterIds,
                             AppendPointer(path, property.Key)))
                {
                    yield return diagnostic;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                foreach (var diagnostic in RelationshipEffectReferenceDiagnostics(
                             array[index],
                             characterIds,
                             AppendPointer(path, index.ToString())))
                {
                    yield return diagnostic;
                }
            }
        }
    }

    private static IEnumerable<StoryDiagnostic> ValidateReferenceValue(
        JsonNode? value,
        string path,
        ReferenceRule rule,
        IReadOnlySet<string> registry)
    {
        if (GetString(value) is { } singleReference)
        {
            if (!registry.Contains(singleReference))
            {
                yield return MissingReference(path, singleReference, rule.IsMedia);
            }

            yield break;
        }

        if (value is not JsonArray references)
        {
            yield break;
        }

        for (var index = 0; index < references.Count; index++)
        {
            if (GetString(references[index]) is { } reference && !registry.Contains(reference))
            {
                yield return MissingReference(
                    AppendPointer(path, index.ToString()),
                    reference,
                    rule.IsMedia);
            }
        }
    }

    private static StoryDiagnostic MissingReference(string path, string reference, bool isMedia) =>
        new(
            isMedia ? "media.reference_not_found" : "reference.not_found",
            path,
            $"The referenced id '{reference}' does not exist.");

    private static bool IsGraphRegistryDuplicate(StoryDiagnostic diagnostic)
    {
        if (diagnostic.Code != "id.duplicate" || !diagnostic.Path.EndsWith("/id", StringComparison.Ordinal))
        {
            return false;
        }

        return diagnostic.Path.StartsWith("/sceneNodes/", StringComparison.Ordinal) ||
               diagnostic.Path.StartsWith("/voiceIntents/", StringComparison.Ordinal) ||
               diagnostic.Path.StartsWith("/npcResponses/", StringComparison.Ordinal);
    }

    private static bool IsGraphReferenceDiagnostic(StoryDiagnostic diagnostic)
    {
        if (diagnostic.Code != "reference.not_found")
        {
            return false;
        }

        return diagnostic.Path.Contains("entryNodeId", StringComparison.Ordinal) ||
               diagnostic.Path.Contains("nextNodeId", StringComparison.Ordinal) ||
               diagnostic.Path.Contains("offlineFallbackNodeId", StringComparison.Ordinal) ||
               diagnostic.Path.Contains("voiceIntentRef", StringComparison.Ordinal) ||
               diagnostic.Path.Contains("npcResponseRef", StringComparison.Ordinal) ||
               diagnostic.Path.Contains("fallbackNpcResponseRef", StringComparison.Ordinal);
    }

    private static IEnumerable<StoryDiagnostic> QuestLifecycleDiagnostics(JsonObject bundle)
    {
        var quests = bundle["questDefinitions"] as JsonArray;
        if (quests is null)
        {
            yield break;
        }

        for (var index = 0; index < quests.Count; index++)
        {
            if (quests[index] is not JsonObject quest || GetString(quest["id"]) is not { } questId)
            {
                continue;
            }

            if (!HasQuestTransition(quest["onComplete"], questId, "completed"))
            {
                yield return new StoryDiagnostic(
                    "quest.lifecycle_incomplete",
                    $"/questDefinitions/{index}/onComplete",
                    $"Quest '{questId}' does not write its completed state.");
            }

            if (!HasQuestTransition(quest["onFailure"], questId, "failed"))
            {
                yield return new StoryDiagnostic(
                    "quest.lifecycle_incomplete",
                    $"/questDefinitions/{index}/onFailure",
                    $"Quest '{questId}' does not write its failed state.");
            }

            if (quest["timeLimitWorldClock"] is not null &&
                !HasQuestTransition(quest["onExpire"], questId, "expired"))
            {
                yield return new StoryDiagnostic(
                    "quest.lifecycle_incomplete",
                    $"/questDefinitions/{index}/onExpire",
                    $"Timed quest '{questId}' does not write its expired state.");
            }
        }
    }

    private static bool HasQuestTransition(JsonNode? effectsNode, string questId, string state)
    {
        var selfTransitions = Objects(effectsNode)
            .Where(effect =>
                GetString(effect["op"]) == "quest_state" &&
                GetString(effect["questId"]) == questId)
            .ToArray();

        return selfTransitions.Length == 1 &&
               GetString(selfTransitions[0]["value"]) == state;
    }

    private static IEnumerable<StoryDiagnostic> MediaFileDiagnostics(
        JsonObject bundle,
        string bundlePath)
    {
        var mediaAssets = bundle["mediaAssets"] as JsonArray;
        if (mediaAssets is null)
        {
            yield break;
        }

        var bundleDirectory = Path.GetDirectoryName(Path.GetFullPath(bundlePath))
            ?? throw new DirectoryNotFoundException("Could not resolve the bundle directory.");

        for (var index = 0; index < mediaAssets.Count; index++)
        {
            if (mediaAssets[index] is not JsonObject media || GetString(media["uri"]) is not { } uriText)
            {
                continue;
            }

            string localPath;
            if (Uri.TryCreate(uriText, UriKind.Absolute, out var absoluteUri))
            {
                if (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // These two schemes identify files already owned by the Unity package. Their
                // physical source files are verified by the compiler's catalog-parity tests;
                // the story bundle deliberately does not duplicate large packaged media.
                if (absoluteUri.Scheme.Equals("streaming-assets", StringComparison.OrdinalIgnoreCase) ||
                    absoluteUri.Scheme.Equals("unity-resource", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!absoluteUri.IsFile)
                {
                    yield return new StoryDiagnostic(
                        "media.uri_scheme_unsupported",
                        $"/mediaAssets/{index}/uri",
                        $"Media URI scheme '{absoluteUri.Scheme}' is not supported.");
                    continue;
                }

                if (absoluteUri.IsUnc ||
                    (!string.IsNullOrEmpty(absoluteUri.Host) &&
                     !absoluteUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return new StoryDiagnostic(
                        "media.path_outside_root",
                        $"/mediaAssets/{index}/uri",
                        "Network file media paths are not allowed.");
                    continue;
                }

                localPath = Path.GetFullPath(absoluteUri.LocalPath);
            }
            else
            {
                localPath = ResolveRelativeMediaPath(bundleDirectory, uriText);
            }

            if (!IsWithinDirectory(bundleDirectory, localPath))
            {
                yield return new StoryDiagnostic(
                    "media.path_outside_root",
                    $"/mediaAssets/{index}/uri",
                    $"The local media path '{uriText}' leaves the bundle directory.");
                continue;
            }

            if (!File.Exists(localPath))
            {
                yield return new StoryDiagnostic(
                    "media.file_missing",
                    $"/mediaAssets/{index}/uri",
                    $"The local media file '{uriText}' does not exist.");
            }
        }
    }

    private static string ResolveRelativeMediaPath(string bundleDirectory, string uriText)
    {
        var queryOrFragment = uriText.IndexOfAny(['?', '#']);
        var pathPart = queryOrFragment >= 0 ? uriText[..queryOrFragment] : uriText;
        pathPart = Uri.UnescapeDataString(pathPart)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(bundleDirectory, pathPart));
    }

    private static bool IsWithinDirectory(string directory, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(directory),
            Path.GetFullPath(candidatePath));

        return relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private static IEnumerable<StoryDiagnostic> GraphDiagnostics(JsonObject bundle)
    {
        var nodes = CreateNodeInfos(bundle);
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var voiceIntents = IndexById(bundle["voiceIntents"]);
        var npcResponses = IndexById(bundle["npcResponses"]);
        var edges = BuildEdges(nodes, nodesById, voiceIntents, npcResponses);
        var entryNodeId = GetString(bundle["entryNodeId"]);
        var reachable = ReachableFrom(entryNodeId, edges);

        foreach (var node in nodes.Where(node => !reachable.Contains(node.Id)))
        {
            yield return new StoryDiagnostic(
                "graph.node_unreachable",
                $"/sceneNodes/{node.Index}/id",
                $"Scene node '{node.Id}' is not reachable from the entry node.");
        }

        foreach (var node in nodes.Where(node => reachable.Contains(node.Id) && !node.Terminal))
        {
            if (edges[node.Id].Count == 0)
            {
                yield return new StoryDiagnostic(
                    "graph.node_no_exit",
                    $"/sceneNodes/{node.Index}/choices",
                    $"Scene node '{node.Id}' is not terminal and has no valid outgoing edge.");
            }

            var hasVoiceInput = StringValues(node.Json["voiceIntentRefs"]).Any();
            var hasFixedChoiceExit = Objects(node.Json["choices"])
                .Select(choice => GetString(choice["nextNodeId"]))
                .Any(nextNodeId => nextNodeId is not null && nodesById.ContainsKey(nextNodeId));
            var hasOfflineNode = GetString(node.Json["offlineFallbackNodeId"]) is { } offlineNode &&
                                 nodesById.ContainsKey(offlineNode);

            if (hasVoiceInput && !hasFixedChoiceExit && !hasOfflineNode)
            {
                yield return new StoryDiagnostic(
                    "offline.fallback_missing",
                    $"/sceneNodes/{node.Index}/offlineFallbackNodeId",
                    $"Voice-enabled scene node '{node.Id}' has no fixed or offline fallback path.");
            }
        }

        var canReachTerminal = NodesThatCanReachTerminal(nodes, edges);
        foreach (var component in StronglyConnectedComponents(nodes, edges, reachable))
        {
            var hasCycle = component.Count > 1 || edges[component[0].Id].Contains(component[0].Id);
            if (!hasCycle || component.Any(node => canReachTerminal.Contains(node.Id)))
            {
                continue;
            }

            var firstNode = component.OrderBy(node => node.Index).First();
            yield return new StoryDiagnostic(
                "graph.cycle_without_termination",
                $"/sceneNodes/{firstNode.Index}/id",
                $"The cycle containing '{firstNode.Id}' cannot reach a terminal node.");
        }
    }

    private static IReadOnlyList<NodeInfo> CreateNodeInfos(JsonObject bundle)
    {
        var result = new List<NodeInfo>();
        if (bundle["sceneNodes"] is not JsonArray nodes)
        {
            return result;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is JsonObject node && GetString(node["id"]) is { } id)
            {
                result.Add(new NodeInfo(
                    id,
                    index,
                    node,
                    node["terminal"]?.GetValue<bool>() == true));
            }
        }

        return result;
    }

    private static Dictionary<string, JsonObject> IndexById(JsonNode? collection)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in Objects(collection))
        {
            if (GetString(item["id"]) is { } id)
            {
                result.TryAdd(id, item);
            }
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> BuildEdges(
        IReadOnlyList<NodeInfo> nodes,
        IReadOnlyDictionary<string, NodeInfo> nodesById,
        IReadOnlyDictionary<string, JsonObject> voiceIntents,
        IReadOnlyDictionary<string, JsonObject> npcResponses)
    {
        var edges = nodes.ToDictionary(
            node => node.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var choice in Objects(node.Json["choices"]))
            {
                AddTarget(node.Id, GetString(choice["nextNodeId"]));
            }

            foreach (var transition in Objects(node.Json["transitions"]))
            {
                AddTarget(node.Id, GetString(transition["nextNodeId"]));
            }

            AddTarget(node.Id, GetString(node.Json["offlineFallbackNodeId"]));

            foreach (var intentId in StringValues(node.Json["voiceIntentRefs"]))
            {
                if (!voiceIntents.TryGetValue(intentId, out var intent))
                {
                    continue;
                }

                AddResponseTarget(node.Id, GetString(intent["npcResponseRef"]));
                AddResponseTarget(node.Id, GetString(intent["fallbackNpcResponseRef"]));
            }

            foreach (var responseId in StringValues(node.Json["npcResponseRefs"]))
            {
                AddResponseTarget(node.Id, responseId);
            }

            if (node.Json["invalid_input_rules"] is JsonObject invalidInputRules)
            {
                foreach (var rule in invalidInputRules)
                {
                    if (rule.Value is JsonObject ruleObject)
                    {
                        AddResponseTarget(node.Id, GetString(ruleObject["npcResponseRef"]));
                    }
                }
            }
        }

        return edges;

        void AddResponseTarget(string sourceNodeId, string? responseId)
        {
            if (responseId is not null && npcResponses.TryGetValue(responseId, out var response))
            {
                AddTarget(sourceNodeId, GetString(response["nextNodeId"]));
            }
        }

        void AddTarget(string sourceNodeId, string? targetNodeId)
        {
            if (targetNodeId is not null && nodesById.ContainsKey(targetNodeId))
            {
                edges[sourceNodeId].Add(targetNodeId);
            }
        }
    }

    private static HashSet<string> ReachableFrom(
        string? entryNodeId,
        IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (entryNodeId is null || !edges.ContainsKey(entryNodeId))
        {
            return reachable;
        }

        var pending = new Queue<string>();
        pending.Enqueue(entryNodeId);
        reachable.Add(entryNodeId);

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var target in edges[nodeId])
            {
                if (reachable.Add(target))
                {
                    pending.Enqueue(target);
                }
            }
        }

        return reachable;
    }

    private static HashSet<string> NodesThatCanReachTerminal(
        IReadOnlyList<NodeInfo> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var reverseEdges = nodes.ToDictionary(
            node => node.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            foreach (var target in edge.Value)
            {
                reverseEdges[target].Add(edge.Key);
            }
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var terminal in nodes.Where(node => node.Terminal))
        {
            result.Add(terminal.Id);
            pending.Enqueue(terminal.Id);
        }

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var source in reverseEdges[nodeId])
            {
                if (result.Add(source))
                {
                    pending.Enqueue(source);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<NodeInfo>> StronglyConnectedComponents(
        IReadOnlyList<NodeInfo> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        IReadOnlySet<string> includedNodeIds)
    {
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var nextIndex = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<IReadOnlyList<NodeInfo>>();

        foreach (var node in nodes.Where(node => includedNodeIds.Contains(node.Id)))
        {
            if (!indices.ContainsKey(node.Id))
            {
                StrongConnect(node.Id);
            }
        }

        return components;

        void StrongConnect(string nodeId)
        {
            indices[nodeId] = nextIndex;
            lowLinks[nodeId] = nextIndex;
            nextIndex++;
            stack.Push(nodeId);
            onStack.Add(nodeId);

            foreach (var target in edges[nodeId].Where(includedNodeIds.Contains))
            {
                if (!indices.ContainsKey(target))
                {
                    StrongConnect(target);
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], indices[target]);
                }
            }

            if (lowLinks[nodeId] != indices[nodeId])
            {
                return;
            }

            var component = new List<NodeInfo>();
            string poppedNodeId;
            do
            {
                poppedNodeId = stack.Pop();
                onStack.Remove(poppedNodeId);
                component.Add(nodesById[poppedNodeId]);
            }
            while (poppedNodeId != nodeId);

            components.Add(component);
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static IEnumerable<string> StringValues(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(GetString).OfType<string>()
            : [];

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string AppendPointer(string path, string segment) =>
        $"{path}/{segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    private sealed record ReferenceRule(string Registry, bool IsMedia = false);

    private sealed record NodeInfo(string Id, int Index, JsonObject Json, bool Terminal);

    private sealed record SchemaCatalog(JsonSchema BundleSchema, IReadOnlyList<JsonSchema> Schemas);
}
