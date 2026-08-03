using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx508VisualBibleContractTests
{
    private static readonly string[] RequiredIdentityIds =
    [
        "identity.shen_yan.v1",
        "identity.chu_mingqi.v1",
        "identity.shi_jun.v1",
        "identity.celadon_scout.v1",
        "identity.cavern_ally.v1",
        "identity.formation_spirit.v1",
        "creature.ink_dragon.v1",
    ];

    private static readonly string[] RequiredFirstFrameIds =
    [
        "firstframe.node.camp.v3",
        "firstframe.node.alliance.v3",
        "firstframe.node.corpse_signs.v3",
        "firstframe.node.rescue.v3",
        "firstframe.shijun.negotiation.v3",
        "firstframe.node.formation.v3",
        "firstframe.node.combat_one.v3",
        "firstframe.node.combat_two.v3",
        "firstframe.node.aftermath.v3",
        "firstframe.node.ending.v3",
        "firstframe.dialogue.scout_mist.v3",
        "firstframe.dialogue.scout_alliance.v3",
        "firstframe.dialogue.scout_rescue.v3",
        "firstframe.dialogue.formation_spirit.v3",
        "firstframe.dialogue.cavern_ally.v3",
    ];

    [Theory]
    [InlineData("unknown_reference", "reference.unknown")]
    [InlineData("hash_mismatch", "reference.hash_mismatch")]
    [InlineData("identity_reuse", "reference.identity_reuse")]
    [InlineData("missing_role", "reference.role_missing")]
    [InlineData("unverified_commercial", "reference.unverified_rights")]
    [InlineData("adopted", "candidate.not_review_only")]
    [InlineData("ship", "candidate.not_review_only")]
    [InlineData("runtime_path", "candidate.runtime_path")]
    [InlineData("missing_frame", "coverage.first_frames")]
    [InlineData("unversioned", "candidate.versioned_name")]
    [InlineData("non_uniform", "candidate.non_uniform_scale")]
    [InlineData("missing_continuity", "candidate.continuity_contract")]
    public async Task CliRejectsUnsafeVisualBibleMutations(string mutation, string expectedCode)
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            ApplyMutation(fixture.Manifest, fixture.Catalog, mutation);
            await File.WriteAllTextAsync(fixture.ManifestPath, fixture.Manifest.ToJsonString());
            await File.WriteAllTextAsync(fixture.CatalogPath, fixture.Catalog.ToJsonString());

            var result = await RunValidatorAsync(fixture);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task CliRejectsInvalidPngSignature()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var first = fixture.Manifest["candidates"]![0]!;
            var path = Path.Combine(fixture.Root, ToPlatformPath(first["outputPath"]!.GetValue<string>()));
            await File.WriteAllBytesAsync(path, new byte[32]);
            UpdateArtifact(first, path, 1600, 900);
            await File.WriteAllTextAsync(fixture.ManifestPath, fixture.Manifest.ToJsonString());

            var result = await RunValidatorAsync(fixture);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("candidate.png_signature", result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task CliRejectsNonSixteenByNineOutput()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var first = fixture.Manifest["candidates"]![0]!;
            var path = Path.Combine(fixture.Root, ToPlatformPath(first["outputPath"]!.GetValue<string>()));
            WritePngHeader(path, 1600, 1000);
            UpdateArtifact(first, path, 1600, 1000);
            await File.WriteAllTextAsync(fixture.ManifestPath, fixture.Manifest.ToJsonString());

            var result = await RunValidatorAsync(fixture);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("candidate.aspect_ratio", result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckedInCx508VisualBiblePassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-visual-bible",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-visual-bible.cx508.json"),
            "--catalog",
            ProjectPath("content/reference-library/catalog.json"),
            "--candidate-root",
            ProjectPath("content/visual-candidates/cx508"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-508 visual bible valid: 7 identity anchors, 15 first frames", result.Stdout);
    }

    [Fact]
    public void CheckedInManifestIsCompleteAndReviewOnly()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-bible.cx508.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-508", root.GetProperty("cardId").GetString());
        Assert.True(root.GetProperty("generationPolicy").GetProperty("stillImagesOnly").GetBoolean());
        Assert.False(root.GetProperty("generationPolicy").GetProperty("geminiOrVeoUsed").GetBoolean());

        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(RequiredIdentityIds, candidates
            .Where(asset => asset.GetProperty("assetKind").GetString() is "identity_anchor" or "creature_anchor")
            .Select(asset => asset.GetProperty("assetId").GetString()).ToArray());
        Assert.Equal(RequiredFirstFrameIds, candidates
            .Where(asset => asset.GetProperty("assetKind").GetString() == "first_frame")
            .Select(asset => asset.GetProperty("assetId").GetString()).ToArray());
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("needs_review", candidate.GetProperty("adoptionStatus").GetString());
            Assert.False(candidate.GetProperty("adopted").GetBoolean());
            Assert.False(candidate.GetProperty("shipInBuild").GetBoolean());
            Assert.StartsWith("content/visual-candidates/cx508/", candidate.GetProperty("outputPath").GetString(), StringComparison.Ordinal);
        });
    }

    private static async Task<ProcessResult> RunValidatorAsync(Fixture fixture) =>
        await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-visual-bible",
            "--manifest",
            fixture.ManifestPath,
            "--catalog",
            fixture.CatalogPath,
            "--candidate-root",
            fixture.Root);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var root = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        var catalog = CreateCatalog();
        var manifest = CreateManifest(root, catalog);
        var catalogPath = Path.Combine(root, "catalog.json");
        var manifestPath = Path.Combine(root, "visual-bible.json");
        await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString());
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
        return new Fixture(root, catalogPath, manifestPath, catalog, manifest);
    }

    private static JsonObject CreateCatalog()
    {
        var references = new[]
        {
            Reference("ref-architecture", "architecture", "structure", "by"),
            Reference("ref-material", "artifacts", "material", "by"),
            Reference("ref-weather", "weather", "lighting", "by"),
            Reference("ref-person", "people", "pose", "by"),
            Reference("ref-costume", "costumes_textiles", "surface_detail", "unverified"),
            Reference("ref-animal", "animals", "anatomy", "by"),
        };
        return new JsonObject
        {
            ["schemaVersion"] = "1.0.0",
            ["libraryId"] = "fixture",
            ["referenceOnly"] = true,
            ["shipInBuild"] = false,
            ["minimumAssetsPerCategory"] = 1,
            ["allowedLicenses"] = new JsonArray("by", "unverified"),
            ["categories"] = new JsonArray(references.Select(item => item["category"]!.DeepClone()).ToArray()),
            ["assets"] = new JsonArray(references),
        };
    }

    private static JsonObject Reference(string id, string category, string role, string license)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return new JsonObject
        {
            ["id"] = id,
            ["category"] = category,
            ["title"] = id,
            ["creator"] = "Fixture",
            ["creatorUrl"] = "https://example.com/creator",
            ["source"] = "fixture",
            ["sourcePageUrl"] = "https://example.com/source/" + id,
            ["downloadUrl"] = "https://example.com/download/" + id + ".jpg",
            ["license"] = license,
            ["licenseVersion"] = license == "unverified" ? "unverified" : "4.0",
            ["licenseUrl"] = license == "unverified" ? "https://example.com/unverified" : "https://creativecommons.org/licenses/by/4.0/",
            ["commercialUseAllowed"] = license != "unverified",
            ["modificationsAllowed"] = license != "unverified",
            ["shareAlikeRequired"] = false,
            ["referenceOnly"] = true,
            ["shipInBuild"] = false,
            ["identityReuse"] = false,
            ["referenceRoles"] = new JsonArray(role),
            ["localRelativePath"] = "content/reference-library/raw/fixture/" + id + ".jpg",
            ["mediaType"] = "image/jpeg",
            ["byteLength"] = 1,
            ["sha256"] = hash,
            ["downloadedAtUtc"] = "2026-07-25T00:00:00Z",
            ["curationStatus"] = "needs_review",
            ["coverageTargetIds"] = new JsonArray("category.fixture"),
        };
    }

    private static JsonObject CreateManifest(string root, JsonObject catalog)
    {
        var catalogAssets = catalog["assets"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var candidates = new JsonArray();
        foreach (var id in RequiredIdentityIds)
        {
            var isCreature = id.StartsWith("creature.", StringComparison.Ordinal);
            candidates.Add(Candidate(root, id, isCreature ? "creature_anchor" : "identity_anchor", catalogAssets, isCreature));
        }
        foreach (var id in RequiredFirstFrameIds)
        {
            candidates.Add(Candidate(root, id, "first_frame", catalogAssets, false));
        }

        return new JsonObject
        {
            ["schemaVersion"] = "1.0.0",
            ["cardId"] = "CX-508",
            ["worldId"] = "lingmai-ember-red-mist",
            ["referenceCatalog"] = "content/reference-library/catalog.json",
            ["generationPolicy"] = new JsonObject
            {
                ["stillImagesOnly"] = true,
                ["geminiOrVeoUsed"] = false,
                ["overwriteExistingAssets"] = false,
                ["realPeopleIdentityReuseAllowed"] = false,
                ["adoptionRequiresHumanReview"] = true,
                ["allowUnverifiedPersonalReferences"] = true,
            },
            ["requiredIdentityAnchorIds"] = new JsonArray(RequiredIdentityIds.Select(item => JsonValue.Create(item)).ToArray()),
            ["requiredFirstFrameIds"] = new JsonArray(RequiredFirstFrameIds.Select(item => JsonValue.Create(item)).ToArray()),
            ["candidates"] = candidates,
        };
    }

    private static JsonObject Candidate(
        string root,
        string id,
        string kind,
        IReadOnlyList<JsonObject> references,
        bool creature)
    {
        var directory = kind == "first_frame" ? "first-frames" : "identity-anchors";
        var fileName = id.Replace('.', '_') + ".png";
        var relative = $"content/visual-candidates/cx508/{directory}/{fileName}";
        var local = Path.Combine(root, ToPlatformPath(relative));
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        WritePngHeader(local, 1600, 900);

        var roles = creature
            ? new[] { "structure", "material", "lighting", "anatomy" }
            : new[] { "structure", "material", "lighting", "pose", "surface_detail" };
        var inputs = new JsonArray();
        foreach (var role in roles)
        {
            var reference = references.First(item => item["referenceRoles"]!.AsArray().Any(value => value!.GetValue<string>() == role));
            inputs.Add(new JsonObject
            {
                ["assetId"] = reference["id"]!.GetValue<string>(),
                ["assetSha256"] = reference["sha256"]!.GetValue<string>(),
                ["role"] = role,
            });
        }

        var candidate = new JsonObject
        {
            ["assetId"] = id,
            ["assetKind"] = kind,
            ["generationMode"] = "codex_builtin_imagegen",
            ["adoptionStatus"] = "needs_review",
            ["adopted"] = false,
            ["shipInBuild"] = false,
            ["outputPath"] = relative,
            ["referenceInputs"] = inputs,
            ["prompt"] = "Use case: historical-scene. Create a photoreal live-action candidate with natural skin, real cloth, physical architecture, shared motivated light and stable props. No text, no watermark, no actor likeness, no copied landmark, no stretched anatomy.",
            ["avoidItems"] = new JsonArray("no_text", "no_watermark", "no_actor_likeness", "no_copied_landmark"),
            ["continuityLocks"] = new JsonArray("stable_identity", "stable_prop_count", "grounded_contact", "shared_environment_light"),
            ["postProcess"] = new JsonObject
            {
                ["mode"] = "none",
                ["sourceWidth"] = 1600,
                ["sourceHeight"] = 900,
                ["outputWidth"] = 1600,
                ["outputHeight"] = 900,
            },
            ["reviewChecks"] = ReviewChecks(),
        };
        if (kind == "first_frame")
        {
            candidate["sourceFirstFramePath"] = "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/legacy.png";
            candidate["reuseVideoIds"] = new JsonArray("video.fixture");
        }
        UpdateArtifact(candidate, local, 1600, 900);
        return candidate;
    }

    private static JsonObject ReviewChecks() => new()
    {
        ["skinAndFabric"] = "pending",
        ["weightBearing"] = "pending",
        ["contactShadows"] = "pending",
        ["sharedLighting"] = "pending",
        ["perspectiveScale"] = "pending",
        ["handsAndProps"] = "pending",
        ["noTextOrWatermark"] = "pending",
        ["noLandmarkReplication"] = "pending",
        ["noActorLikeness"] = "pending",
    };

    private static void ApplyMutation(JsonObject manifest, JsonObject catalog, string mutation)
    {
        var candidate = manifest["candidates"]![0]!.AsObject();
        switch (mutation)
        {
            case "unknown_reference":
                candidate["referenceInputs"]![0]!["assetId"] = "ref-missing";
                break;
            case "hash_mismatch":
                candidate["referenceInputs"]![0]!["assetSha256"] = new string('f', 64);
                break;
            case "identity_reuse":
                catalog["assets"]!.AsArray().First(item => item!["category"]!.GetValue<string>() == "people")!["identityReuse"] = true;
                break;
            case "missing_role":
                candidate["referenceInputs"]!.AsArray().RemoveAt(0);
                break;
            case "unverified_commercial":
                var unverified = catalog["assets"]!.AsArray().First(item => item!["license"]!.GetValue<string>() == "unverified")!;
                unverified["commercialUseAllowed"] = true;
                unverified["modificationsAllowed"] = true;
                break;
            case "adopted":
                candidate["adopted"] = true;
                candidate["adoptionStatus"] = "adopted";
                break;
            case "ship":
                candidate["shipInBuild"] = true;
                break;
            case "runtime_path":
                candidate["outputPath"] = "unity/RedMistVerticalSlice/Assets/Resources/candidate-v1.png";
                break;
            case "missing_frame":
                manifest["candidates"]!.AsArray().RemoveAt(manifest["candidates"]!.AsArray().Count - 1);
                break;
            case "unversioned":
                candidate["assetId"] = "identity.shen_yan";
                candidate["outputPath"] = "content/visual-candidates/cx508/identity-anchors/shen_yan.png";
                break;
            case "non_uniform":
                candidate["postProcess"]!["mode"] = "non_uniform_scale";
                break;
            case "missing_continuity":
                candidate["avoidItems"] = new JsonArray("no_watermark");
                candidate["continuityLocks"] = new JsonArray("stable_identity");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static void UpdateArtifact(JsonNode candidate, string path, int width, int height)
    {
        var bytes = File.ReadAllBytes(path);
        candidate["artifact"] = new JsonObject
        {
            ["mediaType"] = "image/png",
            ["byteLength"] = bytes.LongLength,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ["width"] = width,
            ["height"] = height,
        };
    }

    private static void WritePngHeader(string path, int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(bytes, 0);
        bytes[11] = 13;
        new byte[] { 0x49, 0x48, 0x44, 0x52 }.CopyTo(bytes, 12);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        bytes[24] = 8;
        bytes[25] = 2;
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static string ProjectPath(string relativePath) => Path.Combine(
        ReferenceLibraryProcessHarness.RepositoryRoot.FullName,
        ToPlatformPath(relativePath));

    private static string ToPlatformPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private sealed record Fixture(
        string Root,
        string CatalogPath,
        string ManifestPath,
        JsonObject Catalog,
        JsonObject Manifest);
}
