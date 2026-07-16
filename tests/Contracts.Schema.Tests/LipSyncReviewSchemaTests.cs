using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Contracts.Schema.Tests;

public sealed class LipSyncReviewSchemaTests
{
    [Fact]
    public void ReportSchemaIsStrictAndCannotClaimProductionAuthority()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string path = Path.Combine(
            root.FullName,
            "server",
            "Contracts",
            "schemas",
            "lip-sync-review-report.schema.json");
        Assert.True(File.Exists(path), "CX-503 lip-sync review report schema is missing.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement schema = document.RootElement;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal(
            "not_evaluated",
            properties.GetProperty("productionReadiness").GetProperty("const").GetString());
        Assert.Equal(
            "human_review_export_not_promotion_authority",
            properties.GetProperty("authority").GetProperty("const").GetString());
        Assert.Contains(
            "contentHash",
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.False(
            properties.GetProperty("items").GetProperty("items")
                .GetProperty("additionalProperties").GetBoolean());
        Assert.False(
            properties.GetProperty("events").GetProperty("items")
                .GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void PassEventCannotContainAFailedCheck()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string schemaRoot = Path.Combine(root.FullName, "server", "Contracts", "schemas");
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        JsonSchema? target = null;
        foreach (string path in Directory.EnumerateFiles(schemaRoot, "*.schema.json").Order())
        {
            using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(path));
            JsonSchema schema = JsonSchema.Build(schemaDocument.RootElement.Clone(), buildOptions);
            if (Path.GetFileName(path) == "lip-sync-review-report.schema.json") target = schema;
        }

        JsonObject report = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root.FullName,
            "tests",
            "StoryFixtures",
            "valid",
            "lip-sync-review-report.pass.json")) )!.AsObject();
        report["events"]![0]!["checks"]!["lipSyncPassed"] = false;
        EvaluationResults evaluation = target!.Evaluate(
            JsonDocument.Parse(report.ToJsonString()).RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

        Assert.False(evaluation.IsValid);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Video.sln")))
            current = current.Parent;
        return current ?? throw new DirectoryNotFoundException("Could not locate the Video repository root.");
    }
}
