using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace ReferenceLibrary.Tests;

public sealed class ReferenceLibraryContractTests
{
    private static readonly string[] RequiredCategories =
    [
        "landscape",
        "architecture",
        "plants",
        "animals",
        "waters",
        "weather",
        "astronomy",
        "people",
        "costumes_textiles",
        "artifacts",
        "geology_caves",
        "light_fog_fire",
    ];

    [Fact]
    public void RequiredCx506FilesExist()
    {
        var root = ReferenceLibraryProcessHarness.RepositoryRoot.FullName;
        string[] requiredPaths =
        [
            "tools/ReferenceLibrary/ReferenceLibrary.csproj",
            "server/Contracts/schemas/reference-library-catalog.schema.json",
            "content/reference-library/README.md",
            "content/reference-library/acquisition-plan.json",
            "content/reference-library/catalog.json",
            "content/story/red-mist/image-generation-reference-pilots.cx506.json",
        ];

        var missing = requiredPaths
            .Where(path => !File.Exists(Path.Combine(root, ToPlatformPath(path))))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"CX-506 required files are missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void AcquisitionPlanDefinesTwelveBoundedChineseReferenceQueries()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/reference-library/acquisition-plan.json")));
        var root = document.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("https://api.openverse.org/v1/images/", root.GetProperty("discoveryEndpoint").GetString());
        Assert.Equal(["cc0", "pdm", "by"], root.GetProperty("allowedLicenses")
            .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.True(root.GetProperty("commercialUseRequired").GetBoolean());
        Assert.True(root.GetProperty("modificationRequired").GetBoolean());
        Assert.Equal(48, root.GetProperty("minimumTotalAssets").GetInt32());

        var categories = root.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(RequiredCategories, categories.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(categories, item =>
        {
            Assert.True(item.GetProperty("query").GetString()!.Contains("China", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("title", item.GetProperty("queryMatchField").GetString());
            Assert.True(item.GetProperty("requireOnePerQuery").GetBoolean());
            Assert.Equal(4, item.GetProperty("minimumAssets").GetInt32());
            var queries = item.GetProperty("queries").EnumerateArray()
                .Select(query => query.GetString()!).ToArray();
            Assert.True(queries.Length >= 4);
            Assert.All(queries, query =>
                Assert.True(query.Contains("China", StringComparison.OrdinalIgnoreCase)));
            var queryRequiredAnyTerms = item.GetProperty("queryRequiredAnyTerms")
                .EnumerateArray()
                .Select(group => group.EnumerateArray().Select(term => term.GetString()).ToArray())
                .ToArray();
            Assert.Equal(queries.Length, queryRequiredAnyTerms.Length);
            Assert.All(queryRequiredAnyTerms, Assert.NotEmpty);
            Assert.NotEmpty(item.GetProperty("requiredAnyTerms").EnumerateArray());
            Assert.NotEmpty(item.GetProperty("excludedTerms").EnumerateArray());
            if (item.GetProperty("requireChinaEvidence").GetBoolean())
            {
                Assert.NotEmpty(item.GetProperty("chinaEvidenceTerms").EnumerateArray());
            }
        });
    }

    [Fact]
    public void CatalogConformsToStrictSchemaAndDownloadedFilesMatchHashes()
    {
        var schemaPath = ProjectPath("server/Contracts/schemas/reference-library-catalog.schema.json");
        var catalogPath = ProjectPath("content/reference-library/catalog.json");
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath));
        using var catalogDocument = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var schema = JsonSchema.Build(schemaDocument.RootElement, new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        });
        var evaluation = schema.Evaluate(catalogDocument.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });
        Assert.True(evaluation.IsValid, evaluation.ToString());

        var assets = catalogDocument.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.True(assets.Length >= 48, $"Expected at least 48 assets, found {assets.Length}.");
        foreach (var category in RequiredCategories)
        {
            Assert.True(
                assets.Count(asset => asset.GetProperty("category").GetString() == category) >= 4,
                $"Category {category} has fewer than four assets.");
        }

        foreach (var asset in assets)
        {
            var localPath = ProjectPath(asset.GetProperty("localRelativePath").GetString()!);
            Assert.True(File.Exists(localPath), $"Missing reference file: {localPath}");
            using var stream = File.OpenRead(localPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.Equal(asset.GetProperty("sha256").GetString(), actualHash);
        }
    }

    [Theory]
    [InlineData("license", "by-nc", "license.not_allowed")]
    [InlineData("shareAlikeRequired", true, "license.share_alike")]
    [InlineData("localRelativePath", "../escape.jpg", "path.outside_raw")]
    [InlineData("sourcePageUrl", "https://images.google.com/example", "source.discovery_only")]
    public async Task CliRejectsUnsafeCatalogPolicies(string property, object value, string expectedCode)
    {
        var temporaryRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var catalog = CreateCatalog();
            catalog["assets"]![0]![property] = JsonValue.Create(value);
            var catalogPath = Path.Combine(temporaryRoot, "catalog.json");
            await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString());

            var result = await ReferenceLibraryProcessHarness.ValidateAsync(catalogPath, temporaryRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CliRejectsPeopleIdentityReuse()
    {
        var temporaryRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var catalog = CreateCatalog();
            var peopleAsset = catalog["assets"]!.AsArray()
                .Select(item => item!.AsObject())
                .First(item => item["category"]!.GetValue<string>() == "people");
            peopleAsset["identityReuse"] = true;
            var catalogPath = Path.Combine(temporaryRoot, "catalog.json");
            await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString());

            var result = await ReferenceLibraryProcessHarness.ValidateAsync(catalogPath, temporaryRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("people.identity_reuse", result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CheckedInCatalogPassesCliValidation()
    {
        var result = await ReferenceLibraryProcessHarness.ValidateAsync(
            ProjectPath("content/reference-library/catalog.json"),
            ProjectPath("content/reference-library/raw"));

        Assert.Equal(0, result.ExitCode);
        Assert.Matches("Reference catalog valid: [0-9]+ assets", result.Stdout);
        Assert.Contains("valid", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RawReferenceImagesAreIgnoredButPilotImagesAreVersioned()
    {
        var ignored = await ReferenceLibraryProcessHarness.RunGitAsync(
            "check-ignore",
            "--quiet",
            "content/reference-library/raw/sentinel.jpg");
        Assert.Equal(0, ignored.ExitCode);

        var pilot = await ReferenceLibraryProcessHarness.RunGitAsync(
            "check-ignore",
            "--quiet",
            "unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/cx506-architecture-environment-v1.png");
        Assert.NotEqual(0, pilot.ExitCode);
    }

    [Fact]
    public void PilotManifestKeepsFourTraceableAssetsAtNeedsReview()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(ProjectPath("content/reference-library/catalog.json")));
        var knownReferences = catalog.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => asset.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/image-generation-reference-pilots.cx506.json")));

        var assets = manifest.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(
            ["architecture_environment", "landscape_weather", "original_character", "original_spirit_creature"],
            assets.Select(asset => asset.GetProperty("assetType").GetString()).ToArray());

        foreach (var asset in assets)
        {
            Assert.Equal("needs_review", asset.GetProperty("adoptionStatus").GetString());
            Assert.Equal("codex_builtin_imagegen", asset.GetProperty("generationMode").GetString());
            Assert.True(asset.GetProperty("prompt").GetString()!.Length >= 200);
            var references = asset.GetProperty("referenceAssetIds").EnumerateArray()
                .Select(item => item.GetString()!).ToArray();
            Assert.True(references.Length >= 3);
            Assert.All(references, reference => Assert.Contains(reference, knownReferences));

            var outputPath = asset.GetProperty("outputPath").GetString()!;
            Assert.StartsWith("unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/", outputPath, StringComparison.Ordinal);
            var bytes = File.ReadAllBytes(ProjectPath(outputPath));
            Assert.True(bytes.Length > 8);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        }
    }

    private static JsonObject CreateCatalog()
    {
        var assets = new JsonArray();
        foreach (var category in RequiredCategories)
        {
            for (var index = 1; index <= 4; index++)
            {
                assets.Add(new JsonObject
                {
                    ["id"] = $"ref-{category.Replace('_', '-')}-{index:000}",
                    ["category"] = category,
                    ["title"] = $"Test {category} {index}",
                    ["creator"] = "Test Creator",
                    ["creatorUrl"] = "https://example.com/creator",
                    ["source"] = "openverse",
                    ["sourcePageUrl"] = $"https://example.com/{category}/{index}",
                    ["downloadUrl"] = $"https://example.com/{category}/{index}.jpg",
                    ["license"] = "cc0",
                    ["licenseVersion"] = "1.0",
                    ["licenseUrl"] = "https://creativecommons.org/publicdomain/zero/1.0/",
                    ["commercialUseAllowed"] = true,
                    ["modificationsAllowed"] = true,
                    ["shareAlikeRequired"] = false,
                    ["referenceOnly"] = true,
                    ["shipInBuild"] = false,
                    ["identityReuse"] = false,
                    ["referenceRoles"] = new JsonArray("structure", "material", "lighting"),
                    ["localRelativePath"] = $"content/reference-library/raw/{category}/test-{index:000}.jpg",
                    ["mediaType"] = "image/jpeg",
                    ["byteLength"] = 1,
                    ["sha256"] = new string('0', 64),
                    ["downloadedAtUtc"] = "2026-07-22T00:00:00Z",
                });
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = "1.0.0",
            ["libraryId"] = "cx506-real-reference-library-v1",
            ["referenceOnly"] = true,
            ["shipInBuild"] = false,
            ["minimumAssetsPerCategory"] = 4,
            ["allowedLicenses"] = new JsonArray("cc0", "pdm", "by"),
            ["categories"] = new JsonArray(RequiredCategories
                .Select(category => (JsonNode?)JsonValue.Create(category))
                .ToArray()),
            ["assets"] = assets,
        };
    }

    private static string ProjectPath(string relativePath) => Path.Combine(
        ReferenceLibraryProcessHarness.RepositoryRoot.FullName,
        ToPlatformPath(relativePath));

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
