using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx517MultiviewContractTests
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
    public async Task CheckedInCx517MultiviewCandidatesPassStrictValidation()
    {
        var result = await RunValidatorAsync(
            ProjectPath("content/story/red-mist/red-mist-multiview-candidates.cx517.json"),
            ProjectPath("content/story/red-mist/red-mist-art-direction.cx516.json"),
            ProjectPath("content/visual-candidates/cx517/turnarounds"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "CX-517 multiview candidates valid: 12 outfit boards, 1 ink dragon board, all pending human review",
            result.Stdout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CheckedInManifestMatchesApprovedOutputOrderAndReviewBoundary()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-multiview-candidates.cx517.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-517", root.GetProperty("cardId").GetString());
        Assert.Equal(ExpectedAssetIds, root.GetProperty("requiredCandidateIds")
            .EnumerateArray().Select(item => item.GetString()).ToArray());

        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(13, candidates.Length);
        Assert.Equal(12, candidates.Count(candidate => candidate.GetProperty("assetKind").GetString() == "outfit_turnaround"));
        Assert.Single(candidates.Where(candidate => candidate.GetProperty("assetKind").GetString() == "creature_turnaround"));
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("codex_builtin_imagegen", candidate.GetProperty("generationMode").GetString());
            Assert.Equal("needs_review", candidate.GetProperty("adoptionStatus").GetString());
            Assert.False(candidate.GetProperty("adopted").GetBoolean());
            Assert.False(candidate.GetProperty("shipInBuild").GetBoolean());
            Assert.StartsWith(
                "content/visual-candidates/cx517/turnarounds/",
                candidate.GetProperty("outputPath").GetString(),
                StringComparison.Ordinal);
        });

        var policy = root.GetProperty("generationPolicy");
        Assert.True(policy.GetProperty("expressionsDeferredUntilMultiviewApproval").GetBoolean());
        Assert.Equal("paused", policy.GetProperty("cx514Status").GetString());
    }

    [Theory]
    [InlineData("art_direction_hash", "source.art_direction_hash")]
    [InlineData("anchor_hash", "source.anchor_hash")]
    [InlineData("wrong_anchor", "source.anchor_mapping")]
    [InlineData("missing_candidate", "coverage.candidates")]
    [InlineData("wrong_order", "coverage.candidates")]
    [InlineData("wrong_outfit", "coverage.outfits")]
    [InlineData("missing_view", "candidate.views")]
    [InlineData("missing_identity_lock", "candidate.continuity_contract")]
    [InlineData("text_allowed", "candidate.continuity_contract")]
    [InlineData("wrong_generator", "policy.imagegen_only")]
    [InlineData("adopted", "candidate.review_only")]
    [InlineData("shipping", "candidate.review_only")]
    [InlineData("runtime_path", "candidate.runtime_path")]
    [InlineData("unversioned", "candidate.versioned_name")]
    [InlineData("creature_constraint", "creature.anatomy")]
    [InlineData("cx514_resumed", "policy.cx514_paused")]
    [InlineData("expressions_started", "policy.expressions_deferred")]
    [InlineData("artifact_hash", "candidate.file_hash")]
    [InlineData("artifact_size", "candidate.file_metadata")]
    [InlineData("non_png", "candidate.png_signature")]
    public async Task CliRejectsUnsafeCx517Mutations(string mutation, string expectedCode)
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(ProjectPath(
                "content/story/red-mist/red-mist-multiview-candidates.cx517.json")))!.AsObject();
            ApplyMutation(manifest, mutation);
            var manifestPath = Path.Combine(fixtureRoot, "red-mist-multiview-candidates.cx517.json");
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());

            var result = await RunValidatorAsync(
                manifestPath,
                ProjectPath("content/story/red-mist/red-mist-art-direction.cx516.json"),
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
    public async Task CliRejectsTamperedPngSignature()
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var manifestPath = ProjectPath("content/story/red-mist/red-mist-multiview-candidates.cx517.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            var candidates = manifest["candidates"]!.AsArray();
            foreach (var candidate in candidates)
            {
                var fileName = Path.GetFileName(candidate!["outputPath"]!.GetValue<string>());
                File.Copy(
                    ProjectPath("content/visual-candidates/cx517/turnarounds", fileName),
                    Path.Combine(fixtureRoot, fileName));
            }

            var firstFile = Path.Combine(
                fixtureRoot,
                Path.GetFileName(candidates[0]!["outputPath"]!.GetValue<string>()));
            await File.WriteAllBytesAsync(firstFile, new byte[64]);

            var result = await RunValidatorAsync(
                manifestPath,
                ProjectPath("content/story/red-mist/red-mist-art-direction.cx516.json"),
                fixtureRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("candidate.png_signature", result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static void ApplyMutation(JsonObject manifest, string mutation)
    {
        var candidates = manifest["candidates"]!.AsArray();
        switch (mutation)
        {
            case "art_direction_hash":
                manifest["artDirectionSource"]!["sha256"] = new string('0', 64);
                break;
            case "anchor_hash":
                manifest["anchorSources"]![0]!["sha256"] = new string('0', 64);
                break;
            case "wrong_anchor":
                candidates[0]!["sourceAnchor"]!["assetId"] = "identity.chu_mingqi.v2";
                break;
            case "missing_candidate":
                candidates.RemoveAt(0);
                break;
            case "wrong_order":
                var first = candidates[0]!.DeepClone();
                var second = candidates[1]!.DeepClone();
                candidates[0] = second;
                candidates[1] = first;
                break;
            case "wrong_outfit":
                candidates[0]!["outfitId"] = "outfit.unknown.v1";
                break;
            case "missing_view":
                candidates[0]!["views"]!.AsArray().RemoveAt(1);
                break;
            case "missing_identity_lock":
                RemoveString(candidates[0]!["continuityLocks"]!.AsArray(), "same_face");
                break;
            case "text_allowed":
                RemoveString(candidates[0]!["avoidItems"]!.AsArray(), "no_text");
                break;
            case "wrong_generator":
                candidates[0]!["generationMode"] = "flow2api";
                break;
            case "adopted":
                candidates[0]!["adopted"] = true;
                break;
            case "shipping":
                candidates[0]!["shipInBuild"] = true;
                break;
            case "runtime_path":
                candidates[0]!["outputPath"] = "unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/turnaround.png";
                break;
            case "unversioned":
                candidates[0]!["outputPath"] = "content/visual-candidates/cx517/turnarounds/turnaround_outfit_shen_yan_qinglan_travel.png";
                break;
            case "creature_constraint":
                RemoveString(candidates[^1]!["avoidItems"]!.AsArray(), "no_lizard_anatomy");
                break;
            case "cx514_resumed":
                manifest["generationPolicy"]!["cx514Status"] = "resumed";
                break;
            case "expressions_started":
                manifest["generationPolicy"]!["expressionsDeferredUntilMultiviewApproval"] = false;
                break;
            case "artifact_hash":
                candidates[0]!["artifact"]!["sha256"] = new string('0', 64);
                break;
            case "artifact_size":
                candidates[0]!["artifact"]!["width"] = candidates[0]!["artifact"]!["width"]!.GetValue<int>() + 1;
                break;
            case "non_png":
                candidates[0]!["artifact"]!["mediaType"] = "image/jpeg";
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

    private static async Task<ProcessResult> RunValidatorAsync(
        string manifestPath,
        string artDirectionPath,
        string candidateRoot) =>
        await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-multiview-candidates",
            "--manifest",
            manifestPath,
            "--art-direction",
            artDirectionPath,
            "--candidate-root",
            candidateRoot);

    private static string ProjectPath(params string[] segments) =>
        Path.Combine([ReferenceLibraryProcessHarness.RepositoryRoot.FullName, .. segments]);
}
