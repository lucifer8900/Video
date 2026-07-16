using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Contracts.Schema.Tests;

internal sealed class SchemaFixtureValidator
{
    private static readonly IReadOnlyDictionary<string, string> ReferenceTargets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["factRef"] = "storyFacts",
            ["factRefs"] = "storyFacts",
            ["characterId"] = "characterDefinitions",
            ["questId"] = "questDefinitions",
            ["nextNodeId"] = "sceneNodes",
            ["offlineFallbackNodeId"] = "sceneNodes",
            ["entryNodeId"] = "sceneNodes",
            ["currentNodeId"] = "sceneNodes",
            ["voiceIntentRef"] = "voiceIntents",
            ["voiceIntentRefs"] = "voiceIntents",
            ["npcResponseRef"] = "npcResponses",
            ["npcResponseRefs"] = "npcResponses",
            ["combatantRef"] = "combatantStates",
            ["combatantRefs"] = "combatantStates",
            ["encounterId"] = "encounterDefinitions",
            ["mediaRef"] = "mediaAssets",
            ["mediaRefs"] = "mediaAssets",
            ["fallbackMediaRef"] = "mediaAssets",
            ["lipSyncMediaRef"] = "mediaAssets",
            ["generationJobRef"] = "generationJobs",
        };

    private static readonly Regex JsonArrayInMessage = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    private readonly string _fixtureRoot;
    private readonly IReadOnlyDictionary<string, JsonSchema> _schemas;

    private SchemaFixtureValidator(
        string fixtureRoot,
        IReadOnlyDictionary<string, JsonSchema> schemas)
    {
        _fixtureRoot = fixtureRoot;
        _schemas = schemas;
    }

    public static SchemaFixtureValidator Load(string repositoryRoot)
    {
        var schemaRoot = Path.Combine(repositoryRoot, "server", "Contracts", "schemas");
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var schemaPath in Directory.EnumerateFiles(schemaRoot, "*.schema.json").Order())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var schemaElement = document.RootElement.Clone();
            schemas[Path.GetFileName(schemaPath)] = JsonSchema.Build(schemaElement, buildOptions);
        }

        return new SchemaFixtureValidator(
            Path.Combine(repositoryRoot, "tests", "StoryFixtures"),
            schemas);
    }

    public IReadOnlyList<FixtureDiagnostic> Validate(FixtureCase fixture)
    {
        if (!_schemas.TryGetValue(fixture.SchemaFile, out var schema))
        {
            return [new FixtureDiagnostic("schema.not_found", "", fixture.SchemaFile)];
        }

        var fixturePath = Path.Combine(_fixtureRoot, fixture.InstanceFile.Replace('/', Path.DirectorySeparatorChar));
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var evaluation = schema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
        var diagnostics = evaluation.IsValid
            ? []
            : SchemaDiagnostics(evaluation).ToList();

        if (fixture.CheckReferences && diagnostics.Count == 0)
        {
            var root = JsonNode.Parse(document.RootElement.GetRawText());
            if (root is JsonObject bundle)
            {
                diagnostics.AddRange(ReferenceDiagnostics(bundle));
            }
        }

        return diagnostics;
    }

    private static IEnumerable<FixtureDiagnostic> SchemaDiagnostics(EvaluationResults evaluation)
    {
        foreach (var result in Flatten(evaluation).Where(result => !result.IsValid && result.Errors is not null))
        {
            foreach (var error in result.Errors!)
            {
                var basePath = result.InstanceLocation.ToString();
                if (error.Key == "required")
                {
                    foreach (var property in MissingRequiredProperties(error.Value))
                    {
                        yield return new FixtureDiagnostic(
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
                yield return new FixtureDiagnostic($"schema.{keyword}", basePath, error.Value);
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

    private static IEnumerable<FixtureDiagnostic> ReferenceDiagnostics(JsonObject bundle)
    {
        var registries = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var collectionName in ReferenceTargets.Values.Distinct(StringComparer.Ordinal))
        {
            registries[collectionName] = CollectIds(bundle[collectionName]);
        }

        return Visit(bundle, "", registries);
    }

    private static HashSet<string> CollectIds(JsonNode? collection)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (collection is not JsonArray items)
        {
            return ids;
        }

        foreach (var item in items.OfType<JsonObject>())
        {
            if (item["id"]?.GetValue<string>() is { Length: > 0 } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static IEnumerable<FixtureDiagnostic> Visit(
        JsonNode? node,
        string path,
        IReadOnlyDictionary<string, HashSet<string>> registries)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                var propertyPath = AppendPointer(path, property.Key);
                if (ReferenceTargets.TryGetValue(property.Key, out var registryName))
                {
                    foreach (var diagnostic in ValidateReferenceValue(property.Value, propertyPath, registries[registryName]))
                    {
                        yield return diagnostic;
                    }
                }

                foreach (var diagnostic in Visit(property.Value, propertyPath, registries))
                {
                    yield return diagnostic;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                foreach (var diagnostic in Visit(array[index], AppendPointer(path, index.ToString()), registries))
                {
                    yield return diagnostic;
                }
            }
        }
    }

    private static IEnumerable<FixtureDiagnostic> ValidateReferenceValue(
        JsonNode? value,
        string path,
        IReadOnlySet<string> registry)
    {
        if (value is JsonValue single && single.TryGetValue<string>(out var reference))
        {
            if (!registry.Contains(reference))
            {
                yield return new FixtureDiagnostic("reference.not_found", path, reference);
            }

            yield break;
        }

        if (value is not JsonArray references)
        {
            yield break;
        }

        for (var index = 0; index < references.Count; index++)
        {
            if (references[index] is JsonValue item &&
                item.TryGetValue<string>(out var arrayReference) &&
                !registry.Contains(arrayReference))
            {
                yield return new FixtureDiagnostic(
                    "reference.not_found",
                    AppendPointer(path, index.ToString()),
                    arrayReference);
            }
        }
    }

    private static string AppendPointer(string path, string segment) =>
        $"{path}/{segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
}

internal sealed record FixtureManifest(string SchemaVersion, IReadOnlyList<FixtureCase> Cases);

internal sealed record FixtureCase(
    string Name,
    string SchemaFile,
    string InstanceFile,
    bool ExpectedValid,
    bool CheckReferences = false,
    string? ExpectedCode = null,
    string? ExpectedPath = null);

internal sealed record FixtureDiagnostic(string Code, string Path, string Message)
{
    public override string ToString() => $"{Code} {Path}: {Message}";
}
