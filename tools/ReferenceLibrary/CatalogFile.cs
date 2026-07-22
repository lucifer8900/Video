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
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("Catalog path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(catalog, ReferenceJson.Options) + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
