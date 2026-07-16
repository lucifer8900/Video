using System.Text.Json;

namespace Contracts.Schema.Tests;

public sealed class GenerationBatchSchemaTests
{
    [Fact]
    public void RequestAndResultSchemasAreStrictDraft202012Contracts()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        string requestPath = SchemaPath(repositoryRoot, "generation-batch-request.schema.json");
        string resultPath = SchemaPath(repositoryRoot, "generation-batch-result.schema.json");
        Assert.True(File.Exists(requestPath), "CX-502 batch request schema is missing.");
        Assert.True(File.Exists(resultPath), "CX-502 batch result schema is missing.");

        using JsonDocument requestDocument = JsonDocument.Parse(File.ReadAllText(requestPath));
        using JsonDocument resultDocument = JsonDocument.Parse(File.ReadAllText(resultPath));
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            requestDocument.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            resultDocument.RootElement.GetProperty("$schema").GetString());
        Assert.False(requestDocument.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.False(resultDocument.RootElement.GetProperty("additionalProperties").GetBoolean());

        JsonElement shotIds = requestDocument.RootElement
            .GetProperty("properties")
            .GetProperty("shotIds");
        Assert.Equal(1, shotIds.GetProperty("minItems").GetInt32());
        Assert.Equal(20, shotIds.GetProperty("maxItems").GetInt32());
        Assert.True(shotIds.GetProperty("uniqueItems").GetBoolean());
        string[] requestProperties = requestDocument.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(
            requestProperties,
            property => new[] { "provider", "model", "endpoint", "apiKey", "credential", "secret", "g2", "budget", "cost" }
                .Any(fragment => property.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BatchFixturesEnforceSizeTierAndSensitiveFieldBoundaries()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        SchemaFixtureValidator validator = SchemaFixtureValidator.Load(repositoryRoot.FullName);
        FixtureCase[] cases =
        [
            new("valid preview batch request", "generation-batch-request.schema.json", "valid/generation-batch-request.preview.json", true),
            new("valid final batch request", "generation-batch-request.schema.json", "valid/generation-batch-request.final.json", true),
            new("valid reviewed batch result", "generation-batch-result.schema.json", "valid/generation-batch-result.reviewed.json", true),
            new("batch request rejects 21 shots", "generation-batch-request.schema.json", "invalid/generation-batch-request.21-shots.json", false),
            new("batch request rejects duplicate shots", "generation-batch-request.schema.json", "invalid/generation-batch-request.duplicate-shot.json", false),
            new("batch request rejects provider injection", "generation-batch-request.schema.json", "invalid/generation-batch-request.provider.json", false),
            new("final request requires preview source", "generation-batch-request.schema.json", "invalid/generation-batch-request.final-missing-source.json", false),
            new("batch result rejects prompt leak", "generation-batch-result.schema.json", "invalid/generation-batch-result.prompt.json", false),
        ];

        foreach (FixtureCase fixture in cases)
        {
            IReadOnlyList<FixtureDiagnostic> diagnostics = validator.Validate(fixture);
            Assert.Equal(
                fixture.ExpectedValid,
                diagnostics.Count == 0);
        }
    }

    private static string SchemaPath(DirectoryInfo repositoryRoot, string schemaFile) =>
        Path.Combine(repositoryRoot.FullName, "server", "Contracts", "schemas", schemaFile);

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) return directory;
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }
}
