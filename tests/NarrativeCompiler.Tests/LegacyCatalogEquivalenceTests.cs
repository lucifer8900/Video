using System.Security.Cryptography;
using System.Text.Json;
using Lingmai.RedMist;

namespace NarrativeCompiler.Tests;

public sealed class LegacyCatalogEquivalenceTests
{
    private static readonly IReadOnlyDictionary<string, string> CinematicFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CinematicCatalog.GateArrival] = "fmv_gate_arrival.mp4",
            [CinematicCatalog.CelestialFlight] = "fmv_celestial_flight.mp4",
            [CinematicCatalog.HerbCourtyard] = "fmv_herb_courtyard.mp4",
            [CinematicCatalog.SwordVault] = "fmv_sword_vault.mp4",
        };

    private static readonly IReadOnlyDictionary<string, string> NodeIntros =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prologue"] = CinematicCatalog.GateArrival,
            ["flight"] = CinematicCatalog.CelestialFlight,
            ["herb_route"] = CinematicCatalog.HerbCourtyard,
            ["underground"] = CinematicCatalog.SwordVault,
        };

    [Fact]
    public void LegacyOracleHasExpectedBaseline()
    {
        var nodes = LegacyStoryCatalog.Nodes.Values.ToArray();

        Assert.Equal(14, nodes.Length);
        Assert.Equal(21, nodes.Sum(node => node.choices.Count));
        Assert.Equal(["ending"], nodes.Where(node => node.kind == NodeKind.Ending).Select(node => node.id));

        foreach (var (cueId, fileName) in CinematicFiles)
        {
            Assert.True(CinematicCatalog.TryGetCue(cueId, out var cue));
            Assert.Equal(fileName, cue.FileName);
        }

        foreach (var node in nodes)
        {
            Assert.Equal(
                NodeIntros.GetValueOrDefault(node.id),
                CinematicCatalog.GetNodeIntroCueId(node.id));
        }
    }

    [Fact]
    public async Task BundlePreservesNodesChoicesTextAndCinematics()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);

            using var bundle = LoadBundle(outputDirectory);
            var root = bundle.RootElement;
            Assert.Equal("prologue", root.GetProperty("entryNodeId").GetString());
            Assert.Equal("internal_prototype_only", root.GetProperty("distributionStatus").GetString());

            var compiledNodes = root.GetProperty("sceneNodes")
                .EnumerateArray()
                .ToDictionary(node => RequiredString(node, "id"), StringComparer.Ordinal);
            var localized = root.GetProperty("localizations").GetProperty("zh-CN");

            Assert.Equal(LegacyStoryCatalog.Nodes.Count, compiledNodes.Count);
            foreach (var (nodeId, legacy) in LegacyStoryCatalog.Nodes)
            {
                var actual = compiledNodes[nodeId];
                Assert.Equal(legacy.title, Resolve(localized, actual, "titleKey"));
                Assert.Equal(legacy.location, Resolve(localized, actual, "locationKey"));
                Assert.Equal(legacy.speaker, Resolve(localized, actual, "speakerKey"));
                Assert.Equal(legacy.maleText, Resolve(localized, actual, "maleTextKey"));
                Assert.Equal(legacy.femaleText, Resolve(localized, actual, "femaleTextKey"));
                Assert.Equal(legacy.kind.ToString(), RequiredString(actual, "nodeKind"));
                Assert.Equal(legacy.estimatedMinutes, actual.GetProperty("estimatedMinutes").GetInt32());
                Assert.Equal(legacy.background, RequiredString(actual, "backgroundMediaRef"));

                if (string.IsNullOrEmpty(legacy.portrait))
                {
                    Assert.False(actual.TryGetProperty("portraitMediaRef", out _));
                }
                else
                {
                    Assert.Equal(legacy.portrait, RequiredString(actual, "portraitMediaRef"));
                }

                var expectedIntro = CinematicCatalog.GetNodeIntroCueId(nodeId);
                if (expectedIntro is null)
                {
                    Assert.False(actual.TryGetProperty("introMediaRef", out _));
                }
                else
                {
                    Assert.Equal(expectedIntro, RequiredString(actual, "introMediaRef"));
                }

                var actualChoices = actual.GetProperty("choices").EnumerateArray().ToArray();
                Assert.Equal(legacy.choices.Count, actualChoices.Length);
                for (var index = 0; index < legacy.choices.Count; index++)
                {
                    var expectedChoice = legacy.choices[index];
                    var actualChoice = actualChoices[index];
                    Assert.Equal(expectedChoice.action, RequiredString(actualChoice, "actionId"));
                    Assert.Equal(expectedChoice.label, Resolve(localized, actualChoice, "labelKey"));
                    Assert.Equal(expectedChoice.hint, Resolve(localized, actualChoice, "hintKey"));
                }
            }

            var assets = root.GetProperty("mediaAssets")
                .EnumerateArray()
                .ToDictionary(asset => RequiredString(asset, "id"), StringComparer.Ordinal);
            Assert.Equal(11, assets.Count);
            foreach (var (cueId, fileName) in CinematicFiles)
            {
                var cue = assets[cueId];
                Assert.Equal("video", RequiredString(cue, "mediaType"));
                Assert.EndsWith("/" + fileName, RequiredString(cue, "uri"), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BundleGraphPreservesAuthoredChoiceCountAndReachableTerminalSet()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);

            using var bundle = LoadBundle(outputDirectory);
            var root = bundle.RootElement;
            var nodes = root.GetProperty("sceneNodes").EnumerateArray().ToArray();
            var byId = nodes.ToDictionary(node => RequiredString(node, "id"), StringComparer.Ordinal);
            var edges = byId.Keys.ToDictionary(
                id => id,
                _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);

            var authoredChoiceCount = 0;
            foreach (var node in nodes)
            {
                var source = RequiredString(node, "id");
                foreach (var choice in node.GetProperty("choices").EnumerateArray())
                {
                    authoredChoiceCount++;
                    edges[source].Add(RequiredString(choice, "nextNodeId"));
                }

                foreach (var transition in node.GetProperty("transitions").EnumerateArray())
                {
                    edges[source].Add(RequiredString(transition, "nextNodeId"));
                }
            }

            Assert.Equal(LegacyStoryCatalog.Nodes.Values.Sum(node => node.choices.Count), authoredChoiceCount);

            var reachable = Reachable(RequiredString(root, "entryNodeId"), edges);
            Assert.Equal(byId.Keys.OrderBy(id => id, StringComparer.Ordinal), reachable.OrderBy(id => id, StringComparer.Ordinal));

            var expectedTerminals = LegacyStoryCatalog.Nodes.Values
                .Where(node => node.kind == NodeKind.Ending)
                .Select(node => node.id)
                .OrderBy(id => id, StringComparer.Ordinal);
            var actualTerminals = nodes
                .Where(node => node.TryGetProperty("terminal", out var terminal) && terminal.GetBoolean())
                .Select(node => RequiredString(node, "id"))
                .Where(reachable.Contains)
                .OrderBy(id => id, StringComparer.Ordinal);
            Assert.Equal(expectedTerminals, actualTerminals);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BundlePreservesEveryAuthoredAndRuntimeRouteEdge()
    {
        var expectedChoices = new[]
        {
            "aftermath|loot_escape|ending",
            "aftermath|loot_rescue|ending",
            "aftermath|loot_share|ending",
            "alliance|ally_cautious|flight",
            "alliance|ally_open|flight",
            "alliance|ally_refuse|flight",
            "camp|camp_balanced|alliance",
            "camp|camp_protect|alliance",
            "camp|camp_retreat|alliance",
            "prologue|gate_companion|camp",
            "prologue|gate_scout|alliance",
            "prologue|to_camp|camp",
            "rescue|rescue_careful|shijun",
            "rescue|rescue_rush|shijun",
            "rescue|rescue_skip|shijun",
            "shijun|shijun_probe|formation",
            "shijun|shijun_threat|formation",
            "shijun|shijun_trade|formation",
            "underground|meet_cautious|combat_one",
            "underground|meet_wait|combat_one",
            "underground|meet_warn|combat_one",
        };
        var expectedTransitions = new[]
        {
            "combat_one|moon_control|combat_two",
            "combat_one|phoenix_flash|combat_two",
            "combat_one|probe_blades|combat_two",
            "combat_one|protect_ally|combat_two",
            "combat_one|retreat|ending",
            "combat_one|ward_tail|combat_two",
            "combat_two|formation_burst|aftermath",
            "combat_two|hold_line|aftermath",
            "combat_two|joint_strike|aftermath",
            "combat_two|phoenix_ring|aftermath",
            "combat_two|retreat|ending",
            "combat_two|trump_blades|aftermath",
            "corpse_signs|scan_failure|rescue",
            "corpse_signs|scan_perfect|rescue",
            "corpse_signs|scan_success|rescue",
            "flight|flight_failure|corpse_signs",
            "flight|flight_perfect|herb_route",
            "flight|flight_success|herb_route",
            "formation|formation_failure|combat_one",
            "formation|formation_perfect|underground",
            "formation|formation_spike_bypass|underground",
            "formation|formation_success|underground",
            "herb_route|herb_failure|rescue",
            "herb_route|herb_perfect|corpse_signs",
            "herb_route|herb_success|corpse_signs",
        };

        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);
            using var bundle = LoadBundle(outputDirectory);

            var nodes = bundle.RootElement.GetProperty("sceneNodes").EnumerateArray().ToArray();
            var actualChoices = nodes
                .SelectMany(node => node.GetProperty("choices").EnumerateArray().Select(choice =>
                    $"{RequiredString(node, "id")}|{RequiredString(choice, "actionId")}|{RequiredString(choice, "nextNodeId")}"))
                .OrderBy(edge => edge, StringComparer.Ordinal)
                .ToArray();
            var actualTransitions = nodes
                .SelectMany(node => node.GetProperty("transitions").EnumerateArray().Select(transition =>
                    $"{RequiredString(node, "id")}|{RequiredString(transition, "triggerId")}|{RequiredString(transition, "nextNodeId")}"))
                .OrderBy(edge => edge, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedChoices.OrderBy(edge => edge, StringComparer.Ordinal), actualChoices);
            Assert.Equal(expectedTransitions.OrderBy(edge => edge, StringComparer.Ordinal), actualTransitions);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MediaManifestMatchesExistingUnityFilesAndHashes()
    {
        var outputDirectory = CompilerProcessHarness.CreateTemporaryDirectory();
        try
        {
            var result = await CompilerProcessHarness.RunCompilerAsync(outputDirectory);
            Assert.Equal(0, result.ExitCode);

            using var bundle = LoadBundle(outputDirectory);
            var assets = bundle.RootElement.GetProperty("mediaAssets").EnumerateArray().ToArray();
            Assert.Equal(11, assets.Length);

            foreach (var asset in assets)
            {
                var uri = RequiredString(asset, "uri");
                var localPath = ResolveUnityAssetPath(uri);
                Assert.True(File.Exists(localPath), $"Packaged media source is missing: {localPath}");

                var expectedHash = RequiredString(asset, "contentHash");
                var actualHash = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(localPath))).ToLowerInvariant();
                Assert.Equal(expectedHash, actualHash);
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static JsonDocument LoadBundle(string directory) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "story.bundle.json")));

    private static string Resolve(JsonElement localization, JsonElement owner, string keyProperty) =>
        RequiredString(localization, RequiredString(owner, keyProperty));

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Property '{property}' must be a string.");

    private static HashSet<string> Reachable(
        string entryNodeId,
        IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { entryNodeId };
        var pending = new Queue<string>();
        pending.Enqueue(entryNodeId);
        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var target in edges[nodeId])
            {
                if (result.Add(target)) pending.Enqueue(target);
            }
        }

        return result;
    }

    private static string ResolveUnityAssetPath(string uri)
    {
        const string streamingPrefix = "streaming-assets:///";
        const string resourcePrefix = "unity-resource:///";
        string relativePath;
        string basePath;
        if (uri.StartsWith(streamingPrefix, StringComparison.Ordinal))
        {
            relativePath = uri[streamingPrefix.Length..];
            basePath = Path.Combine(
                CompilerProcessHarness.RepositoryRoot.FullName,
                "unity",
                "RedMistVerticalSlice",
                "Assets",
                "StreamingAssets");
        }
        else if (uri.StartsWith(resourcePrefix, StringComparison.Ordinal))
        {
            relativePath = uri[resourcePrefix.Length..];
            basePath = Path.Combine(
                CompilerProcessHarness.RepositoryRoot.FullName,
                "unity",
                "RedMistVerticalSlice",
                "Assets",
                "Resources");
        }
        else
        {
            throw new Xunit.Sdk.XunitException($"Unsupported packaged media URI: {uri}");
        }

        return Path.Combine(basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
