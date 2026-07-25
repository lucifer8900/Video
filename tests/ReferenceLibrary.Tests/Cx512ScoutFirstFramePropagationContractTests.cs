using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx512ScoutFirstFramePropagationContractTests
{
    [Fact]
    public async Task CheckedInCx512PropagationPassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-scout-first-frame-propagation",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-scout-first-frame-propagation.cx512.json"),
            "--repository-root",
            ReferenceLibraryProcessHarness.RepositoryRoot.FullName);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-512 scout first-frame propagation valid: 3 candidates", result.Stdout);
    }

    [Fact]
    public void CheckedInPropagationIsReviewOnlyAndPreservesGrandV2Sources()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-scout-first-frame-propagation.cx512.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-512", root.GetProperty("cardId").GetString());
        Assert.Equal("identity.celadon_scout.v3", root.GetProperty("baseIdentityAssetId").GetString());
        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(3, candidates.Length);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("needs_review", candidate.GetProperty("adoptionStatus").GetString());
            Assert.False(candidate.GetProperty("adopted").GetBoolean());
            Assert.False(candidate.GetProperty("shipInBuild").GetBoolean());
            Assert.Contains("_v4.png", candidate.GetProperty("outputPath").GetString(), StringComparison.Ordinal);
            Assert.Contains("_grand_v2.png", candidate.GetProperty("sourceFirstFramePath").GetString(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("adopted", "candidate.not_review_only")]
    [InlineData("runtime_path", "candidate.runtime_path")]
    [InlineData("wrong_identity", "reference.identity_path")]
    [InlineData("missing_scene_lock", "candidate.continuity_contract")]
    [InlineData("source_hash", "reference.hash_mismatch")]
    public async Task CliRejectsUnsafeCx512Mutations(string mutation, string expectedCode)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-scout-first-frame-propagation.cx512.json")))!.AsObject();
        var candidate = root["candidates"]![0]!.AsObject();
        switch (mutation)
        {
            case "adopted":
                candidate["adopted"] = true;
                break;
            case "runtime_path":
                candidate["outputPath"] = "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png";
                break;
            case "wrong_identity":
                candidate["identityReference"]!["path"] = "content/visual-candidates/cx509/identity-anchors/identity_celadon_scout_v2.png";
                break;
            case "missing_scene_lock":
                candidate["continuityLocks"]!.AsArray().RemoveAt(0);
                break;
            case "source_hash":
                candidate["sourceFirstFrame"]!["sha256"] = new string('0', 64);
                break;
        }

        var tempManifest = Path.Combine(Path.GetTempPath(), "cx512-mutated-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tempManifest, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "validate-scout-first-frame-propagation",
                "--manifest",
                tempManifest,
                "--repository-root",
                ReferenceLibraryProcessHarness.RepositoryRoot.FullName);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempManifest);
        }
    }

    private static string ProjectPath(string relative) =>
        Path.Combine(ReferenceLibraryProcessHarness.RepositoryRoot.FullName,
            relative.Replace('/', Path.DirectorySeparatorChar));
}
