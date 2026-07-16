using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NarrativeCompiler.Tests;

public sealed class NarrativeCompilerContractTests
{
    [Fact]
    public async Task CompilerCreatesStoryBundle()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            var bundlePath = Path.Combine(outputDirectory, "story.bundle.json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(
                File.Exists(bundlePath),
                $"NarrativeCompiler returned success but did not create {bundlePath}.{Environment.NewLine}" +
                $"stdout: {result.Stdout}{Environment.NewLine}stderr: {result.Stderr}");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CompilationIsByteDeterministicAndContentHashIsCanonical()
    {
        var firstOutput = CompilerProcessHarness.CreateTemporaryDirectory();
        var secondOutput = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var first = await CompilerProcessHarness.RunCompilerAsync(firstOutput);
            var second = await CompilerProcessHarness.RunCompilerAsync(secondOutput);
            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);

            var firstBytes = File.ReadAllBytes(Path.Combine(firstOutput, "story.bundle.json"));
            var secondBytes = File.ReadAllBytes(Path.Combine(secondOutput, "story.bundle.json"));
            Assert.Equal(firstBytes, secondBytes);

            var root = JsonNode.Parse(firstBytes)?.AsObject()
                ?? throw new InvalidDataException("Compiled bundle root must be an object.");
            var actualHash = root["contentHash"]?.GetValue<string>();
            root.Remove("contentHash");
            var canonicalBytes = CanonicalJson(root);
            var expectedHash = "sha256:" + Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
        }
        finally
        {
            Directory.Delete(firstOutput, recursive: true);
            Directory.Delete(secondOutput, recursive: true);
        }
    }

    [Fact]
    public async Task CompilerRejectsUnapprovedNodeWithoutWritingBundle()
    {
        var sourceCopy = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            File.Copy(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "chapter.source.json"),
                Path.Combine(sourceCopy, "chapter.source.json"));
            File.Copy(
                Path.Combine(CompilerProcessHarness.SourceDirectory, "localization.zh-CN.json"),
                Path.Combine(sourceCopy, "localization.zh-CN.json"));

            var sourcePath = Path.Combine(sourceCopy, "chapter.source.json");
            var source = JsonNode.Parse(File.ReadAllBytes(sourcePath))?.AsObject()
                ?? throw new InvalidDataException("Chapter source root must be an object.");
            source["sceneNodes"]![0]!["approvalStatus"] = "needs_review";
            File.WriteAllText(sourcePath, source.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory, sourceCopy);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "story.bundle.json")));
            Assert.Contains("/sceneNodes/0/approvalStatus", result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sourceCopy, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ApprovedVoiceInteractionRecordsAreCompiledAndValidated()
    {
        var sourceCopy = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            CopyProductionSource(sourceCopy);
            var sourcePath = Path.Combine(sourceCopy, "chapter.source.json");
            var source = JsonNode.Parse(File.ReadAllBytes(sourcePath))?.AsObject()
                ?? throw new InvalidDataException("Chapter source root must be an object.");
            foreach (JsonObject node in source["sceneNodes"]!.AsArray().Select(value => value!.AsObject()))
            {
                node["voiceIntentRefs"] = new JsonArray();
                node.Remove("npcResponseRefs");
                node.Remove("invalid_input_rules");
                node.Remove("offlineFallbackNodeId");
            }
            var firstNode = source["sceneNodes"]![0]!.AsObject();
            var firstMediaId = source["mediaAssets"]![0]!["id"]!.GetValue<string>();
            var responseId = "fixture.response.voice.safe";

            firstNode["voiceIntentRefs"] = new JsonArray("fixture.intent.voice.safe");
            firstNode["npcResponseRefs"] = new JsonArray(responseId);
            firstNode["offlineFallbackNodeId"] = "camp";
            firstNode["invalid_input_rules"] = CompleteInvalidInputRules(responseId);
            source["voiceIntents"] = new JsonArray
            {
                new JsonObject
                {
                    ["schemaVersion"] = "1.0.0",
                    ["id"] = "fixture.intent.voice.safe",
                    ["displayKey"] = "story.red_mist.node.prologue.title",
                    ["topicKeys"] = new JsonArray("story.red_mist.choice.prologue.to_camp.label"),
                    ["toneKey"] = "story.red_mist.node.prologue.speaker",
                    ["minimumConfidence"] = 0.8,
                    ["npcResponseRef"] = responseId,
                    ["effects"] = new JsonArray(),
                    ["approvalStatus"] = "approved",
                },
            };
            source["npcResponses"] = new JsonArray
            {
                new JsonObject
                {
                    ["schemaVersion"] = "1.0.0",
                    ["id"] = responseId,
                    ["textKey"] = "story.red_mist.node.prologue.male_text",
                    ["emotionKey"] = "story.red_mist.node.prologue.speaker",
                    ["lipSyncMediaRef"] = firstMediaId,
                    ["effects"] = new JsonArray(),
                    ["approvalStatus"] = "approved",
                },
            };
            File.WriteAllText(sourcePath, source.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory, sourceCopy);
            Assert.Equal(0, compile.ExitCode);
            var bundle = JsonNode.Parse(File.ReadAllBytes(Path.Combine(outputDirectory, "story.bundle.json")))!.AsObject();
            Assert.Single(bundle["voiceIntents"]!.AsArray());
            Assert.Single(bundle["npcResponses"]!.AsArray());
            Assert.NotNull(bundle["sceneNodes"]![0]!["invalid_input_rules"]);

            var validation = await CompilerProcessHarness.RunValidatorAsync(outputDirectory);
            Assert.Equal(0, validation.ExitCode);
        }
        finally
        {
            Directory.Delete(sourceCopy, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task NeedsReviewVoiceInteractionRecordIsRejectedWithoutWritingBundle()
    {
        var sourceCopy = CompilerProcessHarness.CreateTemporaryDirectory();
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            CopyProductionSource(sourceCopy);
            var sourcePath = Path.Combine(sourceCopy, "chapter.source.json");
            var source = JsonNode.Parse(File.ReadAllBytes(sourcePath))!.AsObject();
            source["npcResponses"] = new JsonArray
            {
                new JsonObject
                {
                    ["schemaVersion"] = "1.0.0",
                    ["id"] = "fixture.response.needs_review",
                    ["textKey"] = "fixture.text.needs_review",
                    ["emotionKey"] = "fixture.emotion.neutral",
                    ["lipSyncMediaRef"] = source["mediaAssets"]![0]!["id"]!.GetValue<string>(),
                    ["effects"] = new JsonArray(),
                    ["approvalStatus"] = "needs_review",
                },
            };
            File.WriteAllText(sourcePath, source.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory, sourceCopy);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "story.bundle.json")));
            Assert.Contains("/npcResponses/0/approvalStatus", result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sourceCopy, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CompiledBundlePassesStoryValidator()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, compile.ExitCode);

            var validation = await CompilerProcessHarness.RunValidatorAsync(outputDirectory);
            Assert.Equal(0, validation.ExitCode);
            using var output = JsonDocument.Parse(validation.Stdout);
            Assert.True(output.RootElement.GetProperty("valid").GetBoolean());
            Assert.Empty(output.RootElement.GetProperty("diagnostics").EnumerateArray());
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CompiledProductionBundleContainsApprovedInvalidInputReactions()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, compile.ExitCode);
            var bundle = JsonNode.Parse(File.ReadAllBytes(Path.Combine(outputDirectory, "story.bundle.json")))!.AsObject();
            JsonArray nodes = bundle["sceneNodes"]!.AsArray();
            JsonArray responses = bundle["npcResponses"]!.AsArray();

            Assert.Equal(14, nodes.Count);
            Assert.All(nodes, node =>
            {
                JsonObject rules = node!["invalid_input_rules"]!.AsObject();
                Assert.Equal(
                    ["abuse", "irrelevant", "low_confidence", "silence", "too_long"],
                    rules.Select(rule => rule.Key).Order(StringComparer.Ordinal).ToArray());
            });
            Assert.Equal(25, responses.Count);
            Assert.All(responses, response => Assert.Empty(response!["effects"]!.AsArray()));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StoryValidatorRejectsTamperedCompiledContentHash()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, compile.ExitCode);

            var bundlePath = Path.Combine(outputDirectory, "story.bundle.json");
            var root = JsonNode.Parse(File.ReadAllBytes(bundlePath))?.AsObject()
                ?? throw new InvalidDataException("Compiled bundle root must be an object.");
            var localizations = root["localizations"]!["zh-CN"]!.AsObject();
            var firstKey = localizations.First().Key;
            localizations[firstKey] = "[tampered]";
            WriteBundle(root, bundlePath, updateContentHash: false);

            var validation = await CompilerProcessHarness.RunValidatorAsync(outputDirectory);
            Assert.Equal(2, validation.ExitCode);
            using var output = JsonDocument.Parse(validation.Stdout);
            Assert.Contains(
                output.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == "content.hash_mismatch" &&
                              diagnostic.GetProperty("path").GetString() == "/contentHash");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StoryValidatorRejectsMissingLocalizationKeyWithValidHash()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, compile.ExitCode);

            var bundlePath = Path.Combine(outputDirectory, "story.bundle.json");
            var root = JsonNode.Parse(File.ReadAllBytes(bundlePath))?.AsObject()
                ?? throw new InvalidDataException("Compiled bundle root must be an object.");
            var missingKey = root["sceneNodes"]![0]!["maleTextKey"]!.GetValue<string>();
            root["localizations"]!["zh-CN"]!.AsObject().Remove(missingKey);
            WriteBundle(root, bundlePath, updateContentHash: true);

            var validation = await CompilerProcessHarness.RunValidatorAsync(outputDirectory);
            Assert.Equal(2, validation.ExitCode);
            using var output = JsonDocument.Parse(validation.Stdout);
            Assert.Contains(
                output.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == "localization.key_not_found" &&
                              diagnostic.GetProperty("path").GetString() == "/sceneNodes/0/maleTextKey");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CompiledBundleRequiresNarrativePresentationFields()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var compile = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, compile.ExitCode);

            var bundlePath = Path.Combine(outputDirectory, "story.bundle.json");
            var root = JsonNode.Parse(File.ReadAllBytes(bundlePath))?.AsObject()
                ?? throw new InvalidDataException("Compiled bundle root must be an object.");
            root["sceneNodes"]![0]!.AsObject().Remove("maleTextKey");
            WriteBundle(root, bundlePath, updateContentHash: true);

            var validation = await CompilerProcessHarness.RunValidatorAsync(outputDirectory);
            Assert.Equal(2, validation.ExitCode);
            Assert.Contains("maleTextKey", validation.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BundleDoesNotLeakInternalOriginalizationMap()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);
            var json = File.ReadAllText(Path.Combine(outputDirectory, "story.bundle.json"));

            Assert.DoesNotContain("source-to-game-name-map", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("originalization", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"sourceName\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"sourceDisplayName\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"sourceTerm\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"sourceVolume\"", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
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

    private static void CopyProductionSource(string destination)
    {
        File.Copy(
            Path.Combine(CompilerProcessHarness.SourceDirectory, "chapter.source.json"),
            Path.Combine(destination, "chapter.source.json"));
        File.Copy(
            Path.Combine(CompilerProcessHarness.SourceDirectory, "localization.zh-CN.json"),
            Path.Combine(destination, "localization.zh-CN.json"));
    }

    private static JsonObject CompleteInvalidInputRules(string npcResponseRef) => new()
    {
        ["abuse"] = new JsonObject { ["npcResponseRef"] = npcResponseRef },
        ["irrelevant"] = new JsonObject { ["npcResponseRef"] = npcResponseRef },
        ["too_long"] = new JsonObject { ["npcResponseRef"] = npcResponseRef },
        ["silence"] = new JsonObject { ["npcResponseRef"] = npcResponseRef },
        ["low_confidence"] = new JsonObject { ["npcResponseRef"] = npcResponseRef },
    };

    private static void WriteBundle(JsonObject root, string path, bool updateContentHash)
    {
        if (updateContentHash)
        {
            root.Remove("contentHash");
            root["contentHash"] = "sha256:" + Convert.ToHexString(SHA256.HashData(CanonicalJson(root))).ToLowerInvariant();
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
                foreach (var item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
