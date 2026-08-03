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
            "validate-visual-bible" => ValidateVisualBible(args[1..]),
            "validate-art-direction" => ValidateArtDirection(args[1..]),
            "validate-multiview-candidates" => ValidateMultiviewCandidates(args[1..]),
            "validate-multiview-adoption" => ValidateMultiviewAdoption(args[1..]),
            "validate-visual-anchors" => ValidateVisualAnchors(args[1..]),
            "validate-visual-anchor-correction" => ValidateVisualAnchorCorrection(args[1..]),
            "validate-visual-anchor-adoption" => ValidateVisualAnchorAdoption(args[1..]),
            "validate-scout-first-frame-propagation" => ValidateScoutFirstFramePropagation(args[1..]),
            "validate-scout-first-frame-adoption" => ValidateScoutFirstFrameAdoption(args[1..]),
            "validate-plan" => ValidatePlan(args[1..]),
            "coverage" => Coverage(args[1..]),
            "curate-architecture" => CurateArchitecture(args[1..]),
            "curate-costumes" => CurateCostumes(args[1..]),
            "acquire" => await AcquireAsync(args[1..]),
            "expand" => await ExpandAsync(args[1..]),
            "private-expand" => await PrivateExpandAsync(args[1..]),
            _ => UnknownCommand(args[0]),
        };
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or HttpRequestException or InvalidOperationException)
    {
        Console.Error.WriteLine($"reference-library.failed: {exception.Message}");
        return 3;
    }
}

static int ValidateArtDirection(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var markdownPath = RequiredPath(options, "--markdown");
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-art-direction.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(ArtDirectionValidator.Validate(manifestPath, markdownPath));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-516 art direction invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-516 art direction valid: 8+ official works, 6 humanoids, 12 outfits, 1 ink dragon, 8 environment domains.");
    return 0;
}

static int ValidateMultiviewCandidates(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var artDirectionPath = RequiredPath(options, "--art-direction");
    var candidateRoot = RequiredPath(options, "--candidate-root");
    var repositoryRoot = FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-multiview-candidates.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(MultiviewCandidateValidator.Validate(
        manifestPath,
        artDirectionPath,
        candidateRoot,
        repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-517 multiview candidates invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-517 multiview candidates valid: 12 outfit boards, 1 ink dragon board, all pending human review");
    return 0;
}

static int ValidateMultiviewAdoption(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var sourceManifestPath = RequiredPath(options, "--source-manifest");
    var candidateRoot = RequiredPath(options, "--candidate-root");
    var repositoryRoot = FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-multiview-adoption.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(MultiviewAdoptionValidator.Validate(
        manifestPath,
        sourceManifestPath,
        candidateRoot,
        repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-518 multiview adoption invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-518 multiview adoption valid: 13 reference anchors approved, expression generation unlocked, 0 runtime assets");
    return 0;
}

static int ValidateVisualBible(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var catalogPath = RequiredPath(options, "--catalog");
    var candidateRoot = RequiredPath(options, "--candidate-root");
    var repositoryRoot = FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-visual-bible.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(VisualBibleValidator.Validate(
        manifestPath,
        catalogPath,
        candidateRoot,
        repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-508 visual bible invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-508 visual bible valid: 7 identity anchors, 15 first frames.");
    return 0;
}

static int ValidateVisualAnchors(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var catalogPath = RequiredPath(options, "--catalog");
    var candidateRoot = RequiredPath(options, "--candidate-root");
    var repositoryRoot = FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-visual-anchors.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(VisualAnchorValidator.Validate(
        manifestPath,
        catalogPath,
        candidateRoot,
        repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-509 visual anchors invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-509 visual anchors valid: 5 anchors.");
    return 0;
}

static int ValidateVisualAnchorCorrection(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var catalogPath = RequiredPath(options, "--catalog");
    var candidateRoot = RequiredPath(options, "--candidate-root");
    var repositoryRoot = FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-visual-anchor-correction.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(VisualAnchorCorrectionValidator.Validate(
        manifestPath,
        catalogPath,
        candidateRoot,
        repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-510 visual anchor correction invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-510 visual anchor correction valid: celadon scout v3.");
    return 0;
}

static int ValidateVisualAnchorAdoption(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var repositoryRoot = options.TryGetValue("--repository-root", out var explicitRoot)
        ? Path.GetFullPath(explicitRoot)
        : FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-visual-anchor-adoption.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(VisualAnchorAdoptionValidator.Validate(manifestPath, repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-511 visual anchor adoption invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-511 visual anchor adoption valid: celadon scout v3.");
    return 0;
}

static int ValidateScoutFirstFramePropagation(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var repositoryRoot = options.TryGetValue("--repository-root", out var explicitRoot)
        ? Path.GetFullPath(explicitRoot)
        : FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-scout-first-frame-propagation.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(ScoutFirstFramePropagationValidator.Validate(manifestPath, repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-512 scout first-frame propagation invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-512 scout first-frame propagation valid: 3 candidates.");
    return 0;
}

static int ValidateScoutFirstFrameAdoption(string[] args)
{
    var options = ParseOptions(args);
    var manifestPath = RequiredPath(options, "--manifest");
    var repositoryRoot = options.TryGetValue("--repository-root", out var explicitRoot)
        ? Path.GetFullPath(explicitRoot)
        : FindRepositoryRoot().FullName;
    var schemaPath = options.TryGetValue("--schema", out var explicitSchema)
        ? Path.GetFullPath(explicitSchema)
        : RepositorySchema("red-mist-scout-first-frame-adoption.schema.json");
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(schemaPath, manifestPath));
    diagnostics.AddRange(ScoutFirstFrameAdoptionValidator.Validate(manifestPath, repositoryRoot));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-513 scout first-frame adoption invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine("CX-513 scout first-frame adoption valid: 3 adopted candidates.");
    return 0;
}

static int ValidatePlan(string[] args)
{
    var options = ParseOptions(args);
    var planPath = RequiredPath(options, "--plan");
    var plan = CatalogFile.LoadExpansionPlan(planPath);
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-expansion-plan.schema.json"), planPath));
    diagnostics.AddRange(ChinaExpansionPlanValidator.Validate(plan));
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }

    if (diagnostics.Count > 0)
    {
        Console.WriteLine($"CX-507 plan invalid: {diagnostics.Count} diagnostic(s).");
        return 2;
    }

    Console.WriteLine($"CX-507 plan valid: {plan.Coverage.ProvinceRegions.Count} province-level regions, {plan.MinimumTotalAssets} asset floor.");
    return 0;
}

static int Coverage(string[] args)
{
    var options = ParseOptions(args);
    var catalogPath = RequiredPath(options, "--catalog");
    var planPath = RequiredPath(options, "--plan");
    var catalog = CatalogFile.Load(catalogPath);
    var plan = CatalogFile.LoadExpansionPlan(planPath);
    var reportPath = RequiredPath(options, "--report");
    var planDiagnostics = new List<CatalogDiagnostic>();
    planDiagnostics.AddRange(CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-expansion-plan.schema.json"), planPath));
    planDiagnostics.AddRange(ChinaExpansionPlanValidator.Validate(plan));
    if (planDiagnostics.Count > 0)
    {
        foreach (var diagnostic in planDiagnostics) Console.WriteLine(diagnostic);
        return 2;
    }

    var catalogSchemaDiagnostics = CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-catalog.schema.json"), catalogPath);
    foreach (var diagnostic in catalogSchemaDiagnostics) Console.WriteLine(diagnostic);

    var report = ExpansionCoverageEvaluator.Evaluate(catalog, plan);
    CatalogFile.WriteAtomic(reportPath, report);
    var reportSchemaDiagnostics = CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-coverage-report.schema.json"), reportPath);
    if (reportSchemaDiagnostics.Count > 0)
    {
        foreach (var diagnostic in reportSchemaDiagnostics) Console.WriteLine(diagnostic);
        return 2;
    }
    foreach (var diagnostic in report.Diagnostics) Console.WriteLine(diagnostic);
    Console.WriteLine($"CX-507 coverage {(report.Passes ? "valid" : "invalid")}: {report.TotalAssets}/{plan.MinimumTotalAssets} assets, {report.FailedTargetIds.Count} failed targets.");
    return report.Passes && catalogSchemaDiagnostics.Count == 0 ? 0 : 2;
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

static int CurateArchitecture(string[] args)
{
    var options = ParseOptions(args);
    var catalogPath = RequiredPath(options, "--catalog");
    var decisionPath = RequiredPath(options, "--decisions");
    var downloadsRoot = RequiredPath(options, "--downloads-root");
    var quarantineRoot = RequiredPath(options, "--quarantine-root");
    var schemaDiagnostics = CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-architecture-curation.schema.json"), decisionPath);
    if (schemaDiagnostics.Count > 0)
    {
        foreach (var diagnostic in schemaDiagnostics) Console.WriteLine(diagnostic);
        return 2;
    }

    var catalog = CatalogFile.Load(catalogPath);
    var decision = CatalogFile.LoadArchitectureCurationDecision(decisionPath);
    var result = ArchitectureCuration.Apply(catalog, decision);
    ArchitectureCuration.QuarantineFiles(result.QuarantinedAssets, downloadsRoot, quarantineRoot);
    CatalogFile.WriteAtomic(catalogPath, result.Catalog);
    Console.WriteLine(
        $"CX-507 architecture curated: {result.Catalog.Assets.Count(asset => asset.Category == "architecture")} kept, " +
        $"{result.QuarantinedAssets.Count} quarantined, {result.Catalog.Assets.Count} catalog assets remain.");
    return 0;
}

static int CurateCostumes(string[] args)
{
    var options = ParseOptions(args);
    var catalogPath = RequiredPath(options, "--catalog");
    var decisionPath = RequiredPath(options, "--decisions");
    var downloadsRoot = RequiredPath(options, "--downloads-root");
    var quarantineRoot = RequiredPath(options, "--quarantine-root");
    var schemaDiagnostics = CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-costume-curation.schema.json"), decisionPath);
    if (schemaDiagnostics.Count > 0)
    {
        foreach (var diagnostic in schemaDiagnostics) Console.WriteLine(diagnostic);
        return 2;
    }

    var catalog = CatalogFile.Load(catalogPath);
    var decision = CatalogFile.LoadCostumeCurationDecision(decisionPath);
    var result = CostumeCuration.Apply(catalog, decision);
    ArchitectureCuration.QuarantineFiles(result.QuarantinedAssets, downloadsRoot, quarantineRoot);
    CatalogFile.WriteAtomic(catalogPath, result.Catalog);
    Console.WriteLine(
        $"CX-507 costumes curated: {result.Catalog.Assets.Count(asset => asset.Category == "costumes_textiles")} kept, " +
        $"{result.QuarantinedAssets.Count} Qing/uncertain-period assets quarantined, " +
        $"{result.Catalog.Assets.Count} catalog assets remain.");
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

static async Task<int> ExpandAsync(string[] args)
{
    var options = ParseOptions(args);
    var planPath = RequiredPath(options, "--plan");
    var catalogPath = RequiredPath(options, "--catalog");
    var downloadsRoot = RequiredPath(options, "--downloads-root");
    var cacheRoot = RequiredPath(options, "--cache-root");
    var plan = CatalogFile.LoadExpansionPlan(planPath);
    HashSet<string>? focusedCategories = null;
    if (options.TryGetValue("--only-category", out var onlyCategory))
    {
        focusedCategories = onlyCategory
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (focusedCategories.Count == 0 ||
            focusedCategories.Any(category => !plan.MinimumAssetsPerCategory.ContainsKey(category)))
        {
            throw new InvalidDataException(
                "--only-category must contain known comma-separated CX-507 category IDs.");
        }
    }
    HashSet<string>? focusedQueryIds = null;
    if (options.TryGetValue("--only-query", out var onlyQuery))
    {
        if (focusedCategories is not { Count: > 0 } && options.ContainsKey("--only-target") is false)
        {
            throw new InvalidDataException("--only-query requires --only-category or --only-target.");
        }

        focusedQueryIds = onlyQuery
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var knownQueryIds = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Where(target =>
                (focusedCategories is { Count: > 0 } &&
                 target.Dimension == "category" && focusedCategories.Contains(target.Target.Id)) ||
                (options.TryGetValue("--only-target", out var targetFilter) &&
                 targetFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(target.TargetId, StringComparer.Ordinal)))
            .SelectMany(target => target.Target.Queries)
            .Select(query => query.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (focusedQueryIds.Count == 0 || focusedQueryIds.Any(id => !knownQueryIds.Contains(id)))
        {
            throw new InvalidDataException(
                "--only-query must contain known comma-separated query IDs from --only-category.");
        }
    }
    HashSet<string>? focusedTargetIds = null;
    if (options.TryGetValue("--only-target", out var onlyTarget))
    {
        focusedTargetIds = onlyTarget
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var knownTargetIds = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Select(target => target.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        if (focusedTargetIds.Count == 0 || focusedTargetIds.Any(id => !knownTargetIds.Contains(id)))
        {
            throw new InvalidDataException(
                "--only-target must contain known comma-separated CX-507 target IDs.");
        }
    }
    var diagnostics = new List<CatalogDiagnostic>();
    diagnostics.AddRange(CatalogFile.ValidateSchema(
        RepositorySchema("reference-library-expansion-plan.schema.json"), planPath));
    diagnostics.AddRange(ChinaExpansionPlanValidator.Validate(plan));
    if (diagnostics.Count > 0)
    {
        foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);
        return 2;
    }

    using var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LingmaiChinaReferenceAtlas", "2.0"));
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CX507ReferenceOnly", "1.0"));
    var catalog = File.Exists(catalogPath)
        ? CatalogFile.Load(catalogPath)
        : new ReferenceCatalog();
    var acquirer = new WikimediaExpansionAcquirer(client);
    var expanded = await acquirer.ExpandAsync(
        plan,
        catalog,
        downloadsRoot,
        cacheRoot,
        checkpoint => CatalogFile.WriteAtomic(catalogPath, checkpoint),
        CancellationToken.None,
        focusedCategories,
        focusedQueryIds,
        focusedTargetIds);
    CatalogFile.WriteAtomic(catalogPath, expanded);
    Console.WriteLine($"CX-507 catalog expanded: {expanded.Assets.Count} assets -> {catalogPath}");
    return 0;
}

static async Task<int> PrivateExpandAsync(string[] args)
{
    var options = ParseOptions(args);
    var planPath = RequiredPath(options, "--plan");
    var outputRoot = RequiredPath(options, "--output-root");
    var manifestPath = RequiredPath(options, "--manifest");
    var category = RequiredValue(options, "--category");
    var perQuery = OptionalInt(options, "--per-query", 1);
    var maxQueries = OptionalInt(options, "--max-queries", 0);
    var repositoryRoot = FindRepositoryRoot().FullName;
    DuckDuckGoPrivateResearchAcquirer.ValidatePrivateOutputRoot(repositoryRoot, outputRoot);
    DuckDuckGoPrivateResearchAcquirer.ValidatePrivateOutputRoot(
        repositoryRoot,
        Path.GetDirectoryName(manifestPath) ?? string.Empty);
    var plan = CatalogFile.LoadExpansionPlan(planPath);
    var diagnostics = ChinaExpansionPlanValidator.Validate(plan);
    if (diagnostics.Count > 0)
    {
        foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);
        return 2;
    }

    var manifest = File.Exists(manifestPath)
        ? CatalogFile.LoadPrivateResearchManifest(manifestPath)
        : PrivateResearchManifest.Empty(category);
    if (manifest.Category != category)
    {
        throw new InvalidDataException(
            $"Private manifest category '{manifest.Category}' does not match requested category '{category}'.");
    }

    using var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LingmaiPrivateResearch", "1.0"));
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PersonalUseOnly", "1.0"));
    var acquirer = new DuckDuckGoPrivateResearchAcquirer(client);
    var expanded = await acquirer.AcquireAsync(
        plan,
        manifest,
        repositoryRoot,
        outputRoot,
        perQuery,
        maxQueries,
        checkpoint => CatalogFile.WriteAtomic(manifestPath, checkpoint),
        CancellationToken.None);
    CatalogFile.WriteAtomic(manifestPath, expanded);
    Console.WriteLine(
        $"CX-507 private personal research expanded: {expanded.Assets.Count} {category} assets -> {manifestPath}");
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

static string RequiredValue(IReadOnlyDictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidDataException($"Missing required option {name}.");
    }

    return value;
}

static int OptionalInt(IReadOnlyDictionary<string, string> options, string name, int fallback)
{
    if (!options.TryGetValue(name, out var value)) return fallback;
    return int.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidDataException($"Option {name} must be an integer.");
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

static string RepositorySchema(string fileName) => Path.Combine(
    FindRepositoryRoot().FullName,
    "server",
    "Contracts",
    "schemas",
    fileName);

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("ReferenceLibrary validate --catalog <catalog.json> --downloads-root <raw-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-visual-bible --manifest <red-mist-visual-bible.json> --catalog <catalog.json> --candidate-root <cx508-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-art-direction --manifest <red-mist-art-direction.json> --markdown <red-mist-art-direction.md> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-multiview-candidates --manifest <red-mist-multiview-candidates.json> --art-direction <red-mist-art-direction.json> --candidate-root <cx517-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-multiview-adoption --manifest <red-mist-multiview-adoption.json> --source-manifest <red-mist-multiview-candidates.json> --candidate-root <cx517-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-visual-anchors --manifest <red-mist-visual-anchors.json> --catalog <catalog.json> --candidate-root <cx509-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-visual-anchor-correction --manifest <red-mist-visual-anchor-correction.json> --catalog <catalog.json> --candidate-root <cx510-dir> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-visual-anchor-adoption --manifest <red-mist-visual-anchor-adoption.json> --repository-root <repo> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-scout-first-frame-propagation --manifest <red-mist-scout-first-frame-propagation.json> --repository-root <repo> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary validate-scout-first-frame-adoption --manifest <red-mist-scout-first-frame-adoption.json> --repository-root <repo> [--schema <schema.json>]");
    Console.WriteLine("ReferenceLibrary acquire --plan <plan.json> --catalog <catalog.json> --downloads-root <raw-dir>");
    Console.WriteLine("ReferenceLibrary validate-plan --plan <china-expansion-plan.json>");
    Console.WriteLine("ReferenceLibrary expand --plan <plan.json> --catalog <catalog.json> --downloads-root <raw-dir> --cache-root <cache-dir> [--only-category <id,id,...>] [--only-query <id,id,...>] [--only-target <id,id,...>]");
    Console.WriteLine("ReferenceLibrary coverage --catalog <catalog.json> --plan <plan.json> --report <report.json>");
    Console.WriteLine("ReferenceLibrary curate-architecture --catalog <catalog.json> --decisions <decisions.json> --downloads-root <raw-dir> --quarantine-root <quarantine-dir>");
    Console.WriteLine("ReferenceLibrary curate-costumes --catalog <catalog.json> --decisions <decisions.json> --downloads-root <raw-dir> --quarantine-root <quarantine-dir>");
    Console.WriteLine("ReferenceLibrary private-expand --plan <plan.json> --category <category> --output-root <private-research-dir> --manifest <manifest.json> [--per-query <1-4>] [--max-queries <0-all>] (personal use only; never ships)");
}
