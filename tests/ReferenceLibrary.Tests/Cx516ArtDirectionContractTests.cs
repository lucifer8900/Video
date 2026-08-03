using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx516ArtDirectionContractTests
{
    private static readonly string[] ExpectedHumanoidIds =
    [
        "character.shen_yan",
        "character.chu_mingqi",
        "character.shi_jun",
        "character.celadon_scout",
        "character.cavern_ally",
        "character.formation_spirit",
    ];

    private static readonly IReadOnlyDictionary<string, int> ExpectedOutfitCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["character.shen_yan"] = 3,
            ["character.chu_mingqi"] = 3,
            ["character.shi_jun"] = 2,
            ["character.celadon_scout"] = 2,
            ["character.cavern_ally"] = 1,
            ["character.formation_spirit"] = 1,
        };

    [Fact]
    public async Task CheckedInCx516ArtDirectionPassesStrictValidation()
    {
        var result = await RunValidatorAsync(
            ProjectPath("content/story/red-mist/red-mist-art-direction.cx516.json"),
            ProjectPath("content/story/red-mist/red-mist-art-direction.cx516.md"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "CX-516 art direction valid: 8+ official works, 6 humanoids, 12 outfits, 1 ink dragon, 8 environment domains",
            result.Stdout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CheckedInManifestMatchesApprovedScope()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-art-direction.cx516.json")));
        var root = manifest.RootElement;

        Assert.Equal("CX-516", root.GetProperty("cardId").GetString());
        Assert.Equal("approved", root.GetProperty("approval").GetProperty("status").GetString());
        Assert.Equal(
            ["tang_grand_ceremonial", "song_elegant_daily", "original_xianxia"],
            root.GetProperty("artDirection").GetProperty("primaryDirection")
                .EnumerateArray().Select(item => item.GetString()).ToArray());

        var scope = root.GetProperty("characterScope");
        Assert.Equal(
            ExpectedHumanoidIds,
            scope.GetProperty("humanoidCharacterIds").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
        Assert.Equal(
            ["creature.ink_dragon"],
            scope.GetProperty("creatureIds").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
        Assert.Equal(12, scope.GetProperty("expressionReferenceSet").GetProperty("countPerHumanoid").GetInt32());

        var costumePlan = root.GetProperty("costumePlan").EnumerateArray().ToArray();
        Assert.Equal(12, costumePlan.Sum(item => item.GetProperty("outfits").GetArrayLength()));
        foreach (var entry in costumePlan)
        {
            var characterId = entry.GetProperty("characterId").GetString()!;
            Assert.Equal(ExpectedOutfitCounts[characterId], entry.GetProperty("outfits").GetArrayLength());
        }

        Assert.Equal(8, root.GetProperty("environmentPlan").GetArrayLength());
        Assert.False(root.GetProperty("productionPolicy").GetProperty("imageGenerationThisCard").GetBoolean());
        Assert.False(root.GetProperty("productionPolicy").GetProperty("videoGenerationThisCard").GetBoolean());
        Assert.Equal("paused", root.GetProperty("productionPolicy").GetProperty("cx514Status").GetString());
    }

    [Theory]
    [InlineData("wrong_direction", "style.direction")]
    [InlineData("insufficient_sources", "research.minimum_sources")]
    [InlineData("missing_observation", "research.observation_coverage")]
    [InlineData("unofficial_source", "research.official_page")]
    [InlineData("identity_reuse", "research.identity_reuse")]
    [InlineData("exact_replication", "research.exact_replication")]
    [InlineData("shipping_reference", "research.reference_only")]
    [InlineData("runtime_reference_path", "research.runtime_path")]
    [InlineData("wrong_scope", "scope.characters")]
    [InlineData("wrong_outfit_count", "costume.stage_count")]
    [InlineData("creature_outfit", "creature.no_outfits")]
    [InlineData("missing_expression", "expression.count")]
    [InlineData("missing_environment", "environment.coverage")]
    [InlineData("missing_prohibition", "style.prohibitions")]
    [InlineData("global_darkness", "style.no_global_darkness")]
    [InlineData("image_generation", "policy.no_generation")]
    [InlineData("resume_cx514", "policy.cx514_paused")]
    [InlineData("markdown_mismatch", "markdown.mismatch")]
    public async Task CliRejectsUnsafeCx516Mutations(string mutation, string expectedCode)
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(ProjectPath(
                "content/story/red-mist/red-mist-art-direction.cx516.json")))!.AsObject();
            var markdown = File.ReadAllText(ProjectPath(
                "content/story/red-mist/red-mist-art-direction.cx516.md"));
            ApplyMutation(manifest, mutation, ref markdown);

            var manifestPath = Path.Combine(fixtureRoot, "red-mist-art-direction.cx516.json");
            var markdownPath = Path.Combine(fixtureRoot, "red-mist-art-direction.cx516.md");
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            await File.WriteAllTextAsync(markdownPath, markdown);

            var result = await RunValidatorAsync(manifestPath, markdownPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static void ApplyMutation(JsonObject manifest, string mutation, ref string markdown)
    {
        switch (mutation)
        {
            case "wrong_direction":
                manifest["artDirection"]!["primaryDirection"]![0] = "generic_dark_fantasy";
                break;
            case "insufficient_sources":
                manifest["researchPolicy"]!["sources"]!.AsArray().RemoveAt(0);
                manifest["researchPolicy"]!["sources"]!.AsArray().RemoveAt(0);
                break;
            case "missing_observation":
                foreach (var source in manifest["researchPolicy"]!["sources"]!.AsArray())
                {
                    RemoveString(source!["observations"]!.AsArray(), "lighting");
                }
                break;
            case "unofficial_source":
                manifest["researchPolicy"]!["sources"]![0]!["officialPageUrl"] = "https://example.com/fan-page";
                break;
            case "identity_reuse":
                manifest["researchPolicy"]!["identityReuse"] = true;
                break;
            case "exact_replication":
                manifest["artDirection"]!["antiCopying"]!["exactCostumeReplicationAllowed"] = true;
                break;
            case "shipping_reference":
                manifest["researchPolicy"]!["shipInBuild"] = true;
                break;
            case "runtime_reference_path":
                manifest["researchPolicy"]!["privateReferenceRoot"] = "unity/RedMistVerticalSlice/Assets/Resources/Cx516";
                break;
            case "wrong_scope":
                manifest["characterScope"]!["humanoidCharacterIds"]!.AsArray().RemoveAt(0);
                break;
            case "wrong_outfit_count":
                manifest["costumePlan"]![0]!["outfits"]!.AsArray().RemoveAt(0);
                break;
            case "creature_outfit":
                manifest["costumePlan"]!.AsArray().Add(new JsonObject
                {
                    ["characterId"] = "creature.ink_dragon",
                    ["tier"] = "creature",
                    ["outfits"] = new JsonArray(),
                });
                break;
            case "missing_expression":
                manifest["characterScope"]!["expressionReferenceSet"]!["emotionIds"]!.AsArray().RemoveAt(0);
                break;
            case "missing_environment":
                manifest["environmentPlan"]!.AsArray().RemoveAt(0);
                break;
            case "missing_prohibition":
                RemoveString(manifest["artDirection"]!["prohibitions"]!.AsArray(), "no_dirty_gray");
                break;
            case "global_darkness":
                manifest["artDirection"]!["tonalPrinciples"]!["noGlobalGloom"] = false;
                break;
            case "image_generation":
                manifest["productionPolicy"]!["imageGenerationThisCard"] = true;
                break;
            case "resume_cx514":
                manifest["productionPolicy"]!["cx514Status"] = "resumed";
                break;
            case "markdown_mismatch":
                markdown = markdown.Replace("共 12 套", "共 11 套", StringComparison.Ordinal);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static void RemoveString(JsonArray array, string value)
    {
        for (var index = array.Count - 1; index >= 0; index--)
        {
            if (array[index]?.GetValue<string>() == value)
            {
                array.RemoveAt(index);
            }
        }
    }

    private static async Task<ProcessResult> RunValidatorAsync(string manifestPath, string markdownPath) =>
        await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-art-direction",
            "--manifest",
            manifestPath,
            "--markdown",
            markdownPath);

    private static string ProjectPath(params string[] segments) =>
        Path.Combine([ReferenceLibraryProcessHarness.RepositoryRoot.FullName, .. segments]);
}
