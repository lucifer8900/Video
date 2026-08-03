using System.Text.Json;
using Json.Schema;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class CatalogFile
{
    public static ReferenceCatalog Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReferenceCatalog>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("Catalog JSON was empty.");
    }

    public static AcquisitionPlan LoadPlan(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AcquisitionPlan>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("Acquisition plan JSON was empty.");
    }

    public static ChinaExpansionPlan LoadExpansionPlan(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ChinaExpansionPlan>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("China expansion plan JSON was empty.");
    }

    public static ArchitectureCurationDecision LoadArchitectureCurationDecision(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ArchitectureCurationDecision>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("Architecture curation decision JSON was empty.");
    }

    public static CostumeCurationDecision LoadCostumeCurationDecision(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CostumeCurationDecision>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("Costume curation decision JSON was empty.");
    }

    public static PrivateResearchManifest LoadPrivateResearchManifest(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PrivateResearchManifest>(json, ReferenceJson.Options)
            ?? throw new InvalidDataException("Private research manifest JSON was empty.");
    }

    public static IReadOnlyList<CatalogDiagnostic> ValidateSchema(string schemaPath, string catalogPath)
    {
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath));
        using var catalogDocument = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var schema = JsonSchema.Build(schemaDocument.RootElement, new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        });
        var result = schema.Evaluate(catalogDocument.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });

        return result.IsValid
            ? []
            : [new CatalogDiagnostic("schema.invalid", "", result.ToString() ?? "Schema validation failed.")];
    }

    public static void WriteAtomic(string path, ReferenceCatalog catalog)
    {
        WriteJsonAtomic(path, catalog);
    }

    public static void WriteAtomic(string path, ExpansionCoverageReport report)
    {
        WriteJsonAtomic(path, report);
    }

    public static void WriteAtomic(string path, PrivateResearchManifest manifest)
    {
        WriteJsonAtomic(path, manifest);
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("Catalog path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, ReferenceJson.Options) + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
