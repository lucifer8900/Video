using System.Net.Http.Headers;
using System.Text.Json;
using Lingmai.RedMist.ReferenceLibrary;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintUsage();
        return args.Length == 0 ? 2 : 0;
    }

    try
    {
        return args[0] switch
        {
            "validate" => Validate(args[1..]),
            "acquire" => await AcquireAsync(args[1..]),
            _ => UnknownCommand(args[0]),
        };
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or HttpRequestException or InvalidOperationException)
    {
        Console.Error.WriteLine($"reference-library.failed: {exception.Message}");
        return 3;
    }
}

static int Validate(string[] args)
{
    var options = ParseOptions(args);
    var catalogPath = RequiredPath(options, "--catalog");
    var downloadsRoot = RequiredPath(options, "--downloads-root");
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : Path.Combine(FindRepositoryRoot().FullName, "server", "Contracts", "schemas", "reference-library-catalog.schema.json");

    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, catalogPath));
    diagnostics.AddRange(CatalogPolicyValidator.Validate(CatalogFile.Load(catalogPath), downloadsRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"Reference catalog invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    var count = CatalogFile.Load(catalogPath).Assets.Count;
    Console.WriteLine($"Reference catalog valid: {count} assets.");
    return 0;
}

static async Task<int> AcquireAsync(string[] args)
{
    var options = ParseOptions(args);
    var planPath = RequiredPath(options, "--plan");
    var catalogPath = RequiredPath(options, "--catalog");
    var downloadsRoot = RequiredPath(options, "--downloads-root");
    var plan = CatalogFile.LoadPlan(planPath);

    using var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LingmaiReferenceLibrary", "1.0"));
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CX506ReferenceOnly", "1.0"));

    var acquirer = new OpenverseReferenceAcquirer(client);
    var catalog = await acquirer.AcquireAsync(plan, downloadsRoot, CancellationToken.None);
    CatalogFile.WriteAtomic(catalogPath, catalog);
    Console.WriteLine($"Reference catalog acquired: {catalog.Assets.Count} assets -> {catalogPath}");
    return 0;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Options must be supplied as --name value pairs.");
        }

        options.Add(args[index], args[index + 1]);
    }

    return options;
}

static string RequiredPath(IReadOnlyDictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidDataException($"Missing required option {name}.");
    }

    return Path.GetFullPath(value);
}

static DirectoryInfo FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        var gitPath = Path.Combine(directory.FullName, ".git");
        if (Directory.Exists(gitPath) || File.Exists(gitPath))
        {
            return directory;
        }
    }

    throw new DirectoryNotFoundException("Could not find repository root.");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("ReferenceLibrary validate --catalog <catalog.json> --downloads-root <raw-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary acquire --plan <plan.json> --catalog <catalog.json> --downloads-root <raw-dir>");
}
