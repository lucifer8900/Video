using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx510VisualAnchorCorrectionContractTests
{
    [Fact]
    public async Task CheckedInCx510CorrectionPassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-visual-anchor-correction",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-visual-anchor-correction.cx510.json"),
            "--catalog",
            ProjectPath("content/reference-library/catalog.json"),
            "--candidate-root",
            ProjectPath("content/visual-candidates/cx510"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-510 visual anchor correction valid: celadon scout v3", result.Stdout);
    }

    [Fact]
    public void CheckedInCorrectionIsNonOverwritingAndReviewOnly()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchor-correction.cx510.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-510", root.GetProperty("cardId").GetString());
        Assert.Equal("identity.celadon_scout.v2", root.GetProperty("baseAssetId").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("identity.celadon_scout.v3", candidate.GetProperty("assetId").GetString());
        Assert.Equal("needs_review", candidate.GetProperty("adoptionStatus").GetString());
        Assert.False(candidate.GetProperty("adopted").GetBoolean());
        Assert.False(candidate.GetProperty("shipInBuild").GetBoolean());
    }

    [Theory]
    [InlineData("adopted", "candidate.not_review_only")]
    [InlineData("runtime_path", "candidate.runtime_path")]
    [InlineData("wrong_version", "candidate.identity")]
    [InlineData("missing_proportion_lock", "candidate.proportion_contract")]
    public async Task CliRejectsUnsafeCx510Mutations(string mutation, string expectedCode)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchor-correction.cx510.json")))!.AsObject();
        var candidate = root["candidate"]!.AsObject();
        switch (mutation)
        {
            case "adopted":
                candidate["adopted"] = true;
                break;
            case "runtime_path":
                candidate["outputPath"] = "unity/RedMistVerticalSlice/Assets/Resources/identity_celadon_scout_v3.png";
                break;
            case "wrong_version":
                candidate["assetId"] = "identity.celadon_scout.v2";
                break;
            case "missing_proportion_lock":
                candidate["continuityLocks"]!.AsArray().RemoveAt(0);
                break;
        }

        var tempManifest = Path.Combine(Path.GetTempPath(), "cx510-mutated-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tempManifest, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "validate-visual-anchor-correction",
                "--manifest",
                tempManifest,
                "--catalog",
                ProjectPath("content/reference-library/catalog.json"),
                "--candidate-root",
                ProjectPath("content/visual-candidates/cx510"));

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
