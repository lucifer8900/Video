using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx518MultiviewAdoptionContractTests
{
    private static readonly string[] ExpectedAssetIds =
    [
        "turnaround.outfit.shen_yan_qinglan_travel.v1",
        "turnaround.outfit.shen_yan_xuanheng_ceremony.v1",
        "turnaround.outfit.shen_yan_yaoyuan_combat.v1",
        "turnaround.outfit.chu_mingqi_jiyue_travel.v1",
        "turnaround.outfit.chu_mingqi_danque_ceremony.v1",
        "turnaround.outfit.chu_mingqi_chixiao_combat.v1",
        "turnaround.outfit.shi_jun_cangjin_command.v1",
        "turnaround.outfit.shi_jun_anyao_ritual.v1",
        "turnaround.outfit.celadon_scout_listening.v1",
        "turnaround.outfit.celadon_scout_night_patrol.v1",
        "turnaround.outfit.cavern_ally_stone_lamp.v1",
        "turnaround.outfit.formation_spirit_star_balance.v1",
        "turnaround.creature.ink_dragon.v1",
    ];

    [Fact]
    public async Task CheckedInCx518AdoptionPassesStrictValidation()
    {
        var result = await RunValidatorAsync(
            ProjectPath("content/story/red-mist/red-mist-multiview-adoption.cx518.json"),
            ProjectPath("content/story/red-mist/red-mist-multiview-candidates.cx517.json"),
            ProjectPath("content/visual-candidates/cx517/turnarounds"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "CX-518 multiview adoption valid: 13 reference anchors approved, expression generation unlocked, 0 runtime assets",
            result.Stdout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CheckedInAdoptionRecordsExplicitApprovalWithoutRuntimeShipping()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-multiview-adoption.cx518.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-518", root.GetProperty("cardId").GetString());
        Assert.True(root.GetProperty("humanReview").GetProperty("approved").GetBoolean());
        Assert.Equal("user_explicit_all", root.GetProperty("humanReview").GetProperty("decision").GetString());
        Assert.Equal(ExpectedAssetIds, root.GetProperty("adoptedAssetIds").EnumerateArray().Select(item => item.GetString()).ToArray());

        var policy = root.GetProperty("adoptionPolicy");
        Assert.True(policy.GetProperty("adoptedAsReference").GetBoolean());
        Assert.True(policy.GetProperty("expressionGenerationAllowed").GetBoolean());
        Assert.False(policy.GetProperty("runtimeUse").GetBoolean());
        Assert.False(policy.GetProperty("shipInBuild").GetBoolean());
        Assert.False(policy.GetProperty("overwriteExistingAssets").GetBoolean());
    }

    [Theory]
    [InlineData("manifest_hash", "source.manifest_hash")]
    [InlineData("approval", "review.explicit")]
    [InlineData("missing_asset", "coverage.assets")]
    [InlineData("wrong_order", "coverage.assets")]
    [InlineData("wrong_path", "source.path_mapping")]
    [InlineData("artifact_hash", "source.file_hash")]
    [InlineData("artifact_size", "source.file_metadata")]
    [InlineData("media_type", "source.png_signature")]
    [InlineData("source_adopted", "source.provenance")]
    [InlineData("not_adopted", "adoption.reference")]
    [InlineData("expressions_locked", "adoption.expressions")]
    [InlineData("runtime", "adoption.runtime")]
    [InlineData("shipping", "adoption.shipping")]
    [InlineData("overwrite", "adoption.overwrite")]
    [InlineData("generation", "policy.no_generation")]
    [InlineData("cx514_resumed", "policy.cx514_paused")]
    public async Task CliRejectsUnsafeCx518Mutations(string mutation, string expectedCode)
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(ProjectPath(
                "content/story/red-mist/red-mist-multiview-adoption.cx518.json")))!.AsObject();
            ApplyMutation(manifest, mutation);
            var path = Path.Combine(fixtureRoot, "red-mist-multiview-adoption.cx518.json");
            await File.WriteAllTextAsync(path, manifest.ToJsonString());

            var result = await RunValidatorAsync(
                path,
                ProjectPath("content/story/red-mist/red-mist-multiview-candidates.cx517.json"),
                ProjectPath("content/visual-candidates/cx517/turnarounds"));

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CliRejectsTamperedSourcePng()
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var adoptionPath = ProjectPath("content/story/red-mist/red-mist-multiview-adoption.cx518.json");
            var adoption = JsonNode.Parse(File.ReadAllText(adoptionPath))!.AsObject();
            var assets = adoption["assets"]!.AsArray();
            foreach (var asset in assets)
            {
                var fileName = Path.GetFileName(asset!["sourcePath"]!.GetValue<string>());
                File.Copy(
                    ProjectPath("content/visual-candidates/cx517/turnarounds", fileName),
                    Path.Combine(fixtureRoot, fileName));
            }

            var firstFile = Path.Combine(fixtureRoot, Path.GetFileName(assets[0]!["sourcePath"]!.GetValue<string>()));
            await File.WriteAllBytesAsync(firstFile, new byte[64]);

            var result = await RunValidatorAsync(
                adoptionPath,
                ProjectPath("content/story/red-mist/red-mist-multiview-candidates.cx517.json"),
                fixtureRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("source.png_signature", result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static void ApplyMutation(JsonObject manifest, string mutation)
    {
        var assets = manifest["assets"]!.AsArray();
        switch (mutation)
        {
            case "manifest_hash": manifest["sourceManifest"]!["sha256"] = new string('0', 64); break;
            case "approval": manifest["humanReview"]!["approved"] = false; break;
            case "missing_asset": assets.RemoveAt(0); break;
            case "wrong_order":
                var first = assets[0]!.DeepClone();
                assets[0] = assets[1]!.DeepClone();
                assets[1] = first;
                break;
            case "wrong_path": assets[0]!["sourcePath"] = "content/visual-candidates/cx517/turnarounds/wrong_v1.png"; break;
            case "artifact_hash": assets[0]!["artifact"]!["sha256"] = new string('0', 64); break;
            case "artifact_size": assets[0]!["artifact"]!["byteLength"] = assets[0]!["artifact"]!["byteLength"]!.GetValue<long>() + 1; break;
            case "media_type": assets[0]!["artifact"]!["mediaType"] = "image/jpeg"; break;
            case "source_adopted": assets[0]!["sourceAdoptionStatus"] = "adopted"; break;
            case "not_adopted": assets[0]!["adoptedAsReference"] = false; break;
            case "expressions_locked": manifest["adoptionPolicy"]!["expressionGenerationAllowed"] = false; break;
            case "runtime": manifest["adoptionPolicy"]!["runtimeUse"] = true; break;
            case "shipping": manifest["adoptionPolicy"]!["shipInBuild"] = true; break;
            case "overwrite": manifest["adoptionPolicy"]!["overwriteExistingAssets"] = true; break;
            case "generation": manifest["adoptionPolicy"]!["imagegenUsed"] = true; break;
            case "cx514_resumed": manifest["adoptionPolicy"]!["cx514Status"] = "resumed"; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static async Task<ProcessResult> RunValidatorAsync(string manifestPath, string sourceManifestPath, string candidateRoot) =>
        await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-multiview-adoption",
            "--manifest", manifestPath,
            "--source-manifest", sourceManifestPath,
            "--candidate-root", candidateRoot);

    private static string ProjectPath(params string[] segments) =>
        Path.Combine([ReferenceLibraryProcessHarness.RepositoryRoot.FullName, .. segments]);
}
