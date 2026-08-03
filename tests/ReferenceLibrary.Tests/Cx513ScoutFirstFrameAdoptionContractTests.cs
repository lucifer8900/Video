using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx513ScoutFirstFrameAdoptionContractTests
{
    [Fact]
    public async Task CheckedInCx513AdoptionPassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-scout-first-frame-adoption",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-scout-first-frame-adoption.cx513.json"),
            "--repository-root",
            ReferenceLibraryProcessHarness.RepositoryRoot.FullName);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-513 scout first-frame adoption valid: 3 adopted candidates", result.Stdout);
    }

    [Fact]
    public void CheckedInAdoptionRequiresExplicitApprovalAndNewVersionedRuntimeTargets()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-scout-first-frame-adoption.cx513.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-513", root.GetProperty("cardId").GetString());
        Assert.Equal("CX-512", root.GetProperty("sourceCardId").GetString());
        Assert.True(root.GetProperty("humanReview").GetProperty("approved").GetBoolean());
        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(3, candidates.Length);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("adopted", candidate.GetProperty("adoptionStatus").GetString());
            Assert.True(candidate.GetProperty("adopted").GetBoolean());
            Assert.True(candidate.GetProperty("shipInBuild").GetBoolean());
            Assert.Contains("_v4.png", candidate.GetProperty("targetPath").GetString(), StringComparison.Ordinal);
            Assert.Contains("Generated/VideoFirstFrames/firstframe_dialogue_scout_", candidate.GetProperty("runtimeResourcePath").GetString(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("approval", "approval.required")]
    [InlineData("overwrite", "target.overwrite")]
    [InlineData("runtime_path", "target.runtime_path")]
    [InlineData("source_hash", "source.hash_mismatch")]
    [InlineData("registration", "target.registration")]
    public async Task CliRejectsUnsafeCx513Mutations(string mutation, string expectedCode)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-scout-first-frame-adoption.cx513.json")))!.AsObject();
        var candidate = root["candidates"]![0]!.AsObject();
        switch (mutation)
        {
            case "approval":
                root["humanReview"]!["approved"] = false;
                break;
            case "overwrite":
                candidate["overwriteExistingAssets"] = true;
                break;
            case "runtime_path":
                candidate["targetPath"] = "unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png";
                break;
            case "source_hash":
                candidate["sourceArtifact"]!["sha256"] = new string('0', 64);
                break;
            case "registration":
                candidate["runtimeResourcePath"] = "Generated/VideoFirstFrames/wrong";
                break;
        }

        var tempManifest = Path.Combine(Path.GetTempPath(), "cx513-mutated-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tempManifest, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "validate-scout-first-frame-adoption",
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
