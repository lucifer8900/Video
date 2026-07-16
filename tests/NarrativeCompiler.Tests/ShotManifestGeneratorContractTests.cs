using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NarrativeCompiler.Tests;

public sealed class ShotManifestGeneratorContractTests
{
    [Fact]
    public async Task GeneratorProducesOneFailClosedShotPerBundleNode()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory);
            var manifestPath = Path.Combine(outputDirectory, "shot-manifest.json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(
                File.Exists(manifestPath),
                $"Generator returned success without {manifestPath}.{Environment.NewLine}" +
                $"stdout: {result.Stdout}{Environment.NewLine}stderr: {result.Stderr}");

            var manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!.AsObject();
            Assert.Equal("needs_review", manifest["approvalStatus"]!.GetValue<string>());
            Assert.False(manifest["shotDispatchAllowed"]!.GetValue<bool>());
            Assert.Equal("USD", manifest["currency"]!.GetValue<string>());
            Assert.Equal(25, manifest["responseRequirements"]!.AsArray().Count);

            var shots = manifest["shots"]!.AsArray();
            Assert.Equal(14, shots.Count);
            Assert.Equal(14, shots.Select(shot => shot!["nodeId"]!.GetValue<string>()).Distinct().Count());
            Assert.Equal(4, shots.Count(shot => shot!["generationTier"]!.GetValue<string>() == "reuse_only"));
            Assert.Equal(10, shots.Count(shot => shot!["generationTier"]!.GetValue<string>() == "blocked_pending_g2"));
            Assert.Equal(70, shots.Sum(shot => shot!["npcResponseRefs"]!.AsArray().Count));
            Assert.All(shots, shot =>
            {
                Assert.False(shot!["dispatchAllowed"]!.GetValue<bool>());
                Assert.Equal(0, shot["maximumCostMicros"]!.GetValue<long>());
                Assert.Equal("background_placeholder", shot["firstFrame"]!["origin"]!.GetValue<string>());
                Assert.Equal("background_placeholder", shot["lastFrame"]!["origin"]!.GetValue<string>());
                Assert.Equal("image", shot["firstFrame"]!["mediaType"]!.GetValue<string>());
                Assert.Equal("image", shot["lastFrame"]!["mediaType"]!.GetValue<string>());
                Assert.Equal("background_placeholder", shot["fallbackOrigin"]!.GetValue<string>());
                Assert.Contains(
                    shot["fallbackMedia"]!["mediaType"]!.GetValue<string>(),
                    new[] { "image", "video", "animation" });
                Assert.Equal("unbound_node_text", shot["dialogueBinding"]!.GetValue<string>());
                Assert.Equal(2, shot["dialogueVariants"]!.AsArray().Count);
                Assert.True(shot["source"]!["needsReview"]!.GetValue<bool>());
                Assert.Contains(
                    shot["issueCodes"]!.AsArray(),
                    issue => issue!.GetValue<string>() == "source.original_missing");
                Assert.NotNull(shot["mediaRoles"]!["backgroundMediaRef"]);
            });
            Assert.All(
                shots.Where(shot => shot!["generationTier"]!.GetValue<string>() == "blocked_pending_g2"),
                shot => Assert.Equal("blocked", shot!["readinessStatus"]!.GetValue<string>()));
            Assert.All(
                shots.Where(shot => shot!["generationTier"]!.GetValue<string>() == "reuse_only"),
                shot => Assert.Contains(
                    shot!["primaryMedia"]!["mediaType"]!.GetValue<string>(),
                    new[] { "video", "animation" }));
            Assert.All(manifest["responseRequirements"]!.AsArray(), requirement =>
            {
                Assert.Null(requirement!["dispatchAllowed"]);
                Assert.Equal("blocked", requirement["readinessStatus"]!.GetValue<string>());
                Assert.Equal("image", requirement["lipSyncMedia"]!["mediaType"]!.GetValue<string>());
            });

            AssertCanonicalContentHash(manifest);
            var raw = File.ReadAllText(manifestPath);
            Assert.DoesNotContain("unity-resource://", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("streaming-assets://", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GEMINI_API_KEY", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("source-to-game-name-map", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorIsByteDeterministicAndCopiesBothRoutesExactly()
    {
        var firstOutput = CompilerProcessHarness.CreateTemporaryDirectory();
        var secondOutput = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var first = await CompilerProcessHarness.RunShotManifestGeneratorAsync(firstOutput);
            var second = await CompilerProcessHarness.RunShotManifestGeneratorAsync(secondOutput);
            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);

            var firstBytes = File.ReadAllBytes(Path.Combine(firstOutput, "shot-manifest.json"));
            var secondBytes = File.ReadAllBytes(Path.Combine(secondOutput, "shot-manifest.json"));
            Assert.Equal(firstBytes, secondBytes);

            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            var manifest = JsonNode.Parse(firstBytes)!.AsObject();
            var localization = bundle["localizations"]!.AsObject().Single().Value!.AsObject();
            var shotsByNode = manifest["shots"]!.AsArray().ToDictionary(
                shot => shot!["nodeId"]!.GetValue<string>(),
                shot => shot!.AsObject(),
                StringComparer.Ordinal);

            foreach (var node in bundle["sceneNodes"]!.AsArray())
            {
                var shot = shotsByNode[node!["id"]!.GetValue<string>()];
                var variants = shot["dialogueVariants"]!.AsArray().ToDictionary(
                    variant => variant!["route"]!.GetValue<string>(),
                    variant => variant!.AsObject(),
                    StringComparer.Ordinal);

                AssertDialogue(node, localization, variants["male"], "maleTextKey");
                AssertDialogue(node, localization, variants["female"], "femaleTextKey");
            }
        }
        finally
        {
            Directory.Delete(firstOutput, recursive: true);
            Directory.Delete(secondOutput, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorRejectsTamperedBundleAndPreservesExistingManifest()
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        var manifestPath = Path.Combine(outputDirectory, "shot-manifest.json");
        var sentinel = "existing-approved-output";
        try
        {
            File.Copy(Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json"), bundlePath);
            var bundle = JsonNode.Parse(File.ReadAllBytes(bundlePath))!.AsObject();
            bundle["localizations"]!["zh-CN"]!["story.red_mist.node.prologue.male_text"] = "tampered";
            File.WriteAllText(bundlePath, bundle.ToJsonString());
            File.WriteAllText(manifestPath, sentinel);

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("contentHash", result.Stderr, StringComparison.Ordinal);
            Assert.Empty(result.Stdout);
            Assert.Equal(sentinel, File.ReadAllText(manifestPath));
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorRejectsUnapprovedMediaWithStableJsonPointer()
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        try
        {
            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            bundle["mediaAssets"]![0]!["approvalStatus"] = "needs_review";
            UpdateContentHash(bundle);
            File.WriteAllText(bundlePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("/mediaAssets/0/approvalStatus", result.Stderr, StringComparison.Ordinal);
            Assert.Empty(result.Stdout);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "shot-manifest.json")));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorRejectsSchemaInvalidReferenceAfterCanonicalRehash()
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        try
        {
            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            bundle["sceneNodes"]![0]!["mediaRefs"] = new JsonArray("fixture.media.missing");
            UpdateContentHash(bundle);
            File.WriteAllText(bundlePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("/sceneNodes/0/mediaRefs/0", result.Stderr, StringComparison.Ordinal);
            Assert.Empty(result.Stdout);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "shot-manifest.json")));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorNeverOverwritesItsInputWhenInputUsesOutputFileName()
    {
        var directory = CompilerProcessHarness.CreateTemporaryDirectory();
        var inputPath = Path.Combine(directory, "shot-manifest.json");
        try
        {
            var original = File.ReadAllBytes(Path.Combine(
                CompilerProcessHarness.SourceDirectory,
                "story.bundle.json"));
            File.WriteAllBytes(inputPath, original);

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(directory, inputPath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("same file", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.Stdout);
            Assert.Equal(original, File.ReadAllBytes(inputPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("release_candidate")]
    [InlineData("shipping")]
    public async Task GeneratorAcceptsLaterDistributionStatusButStillFailsClosed(string status)
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        try
        {
            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            bundle["distributionStatus"] = status;
            UpdateContentHash(bundle);
            File.WriteAllText(bundlePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);

            var manifest = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(outputDirectory, "shot-manifest.json")))!.AsObject();
            Assert.Equal("needs_review", manifest["approvalStatus"]!.GetValue<string>());
            Assert.False(manifest["shotDispatchAllowed"]!.GetValue<bool>());
            Assert.All(
                manifest["shots"]!.AsArray(),
                shot => Assert.False(shot!["dispatchAllowed"]!.GetValue<bool>()));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorRejectsNpcResponseOutsideNodeAllowlist()
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        try
        {
            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            var outsideResponse = bundle["npcResponses"]![5]!["id"]!.GetValue<string>();
            bundle["sceneNodes"]![0]!["invalid_input_rules"]!["abuse"]!["npcResponseRef"] =
                outsideResponse;
            UpdateContentHash(bundle);
            File.WriteAllText(bundlePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains(
                "/sceneNodes/0/invalid_input_rules/abuse/npcResponseRef",
                result.Stderr,
                StringComparison.Ordinal);
            Assert.Contains("allowlist", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.Stdout);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "shot-manifest.json")));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratorFailsClosedBeforeWritingSchemaInvalidDerivedManifest()
    {
        var inputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        var bundlePath = Path.Combine(inputDirectory, "story.bundle.json");
        try
        {
            var bundle = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "story.bundle.json")))!.AsObject();
            bundle["mediaAssets"]![0]!["assetVersion"] = new string('v', 81);
            UpdateContentHash(bundle);
            File.WriteAllText(bundlePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory, bundlePath);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("shot-manifest.schema.json", result.Stderr, StringComparison.Ordinal);
            Assert.Empty(result.Stdout);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "shot-manifest.json")));
        }
        finally
        {
            Directory.Delete(inputDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckedInProductionManifestMatchesFreshGenerationByteForByte()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunShotManifestGeneratorAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);

            var expected = File.ReadAllBytes(Path.Combine(
                CompilerProcessHarness.SourceDirectory,
                "shot-manifest.json"));
            var actual = File.ReadAllBytes(Path.Combine(outputDirectory, "shot-manifest.json"));
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static void AssertDialogue(
        JsonNode node,
        JsonObject localization,
        JsonObject actual,
        string keyProperty)
    {
        var expectedKey = node[keyProperty]!.GetValue<string>();
        Assert.Equal(expectedKey, actual["localizationKey"]!.GetValue<string>());
        Assert.Equal(localization[expectedKey]!.GetValue<string>(), actual["text"]!.GetValue<string>());
    }

    private static void AssertCanonicalContentHash(JsonObject manifest)
    {
        var actual = manifest["contentHash"]!.GetValue<string>();
        manifest.Remove("contentHash");
        var expected = "sha256:" + Convert.ToHexString(SHA256.HashData(CanonicalJson(manifest))).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    private static void UpdateContentHash(JsonObject root)
    {
        root.Remove("contentHash");
        root["contentHash"] = "sha256:" +
            Convert.ToHexString(SHA256.HashData(CanonicalJson(root))).ToLowerInvariant();
    }

    private static byte[] CanonicalJson(JsonNode? node)
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
}
