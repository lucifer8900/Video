using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx511VisualAnchorAdoptionContractTests
{
    [Fact]
    public async Task CheckedInCx511AdoptionPassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-visual-anchor-adoption",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-visual-anchor-adoption.cx511.json"),
            "--repository-root",
            ReferenceLibraryProcessHarness.RepositoryRoot.FullName);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-511 visual anchor adoption valid: celadon scout v3", result.Stdout);
    }

    [Fact]
    public void CheckedInAdoptionIsExplicitAndNonOverwriting()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchor-adoption.cx511.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-511", root.GetProperty("cardId").GetString());
        Assert.Equal("identity.celadon_scout.v3", root.GetProperty("sourceAssetId").GetString());
        Assert.True(root.GetProperty("humanReview").GetProperty("approved").GetBoolean());
        Assert.False(root.GetProperty("overwriteExistingAssets").GetBoolean());
        Assert.True(root.GetProperty("shipInBuild").GetBoolean());
        Assert.Equal(
            "unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/identity_celadon_scout_v3.png",
            root.GetProperty("targetPath").GetString());
    }

    [Theory]
    [InlineData("wrong_source_hash", "source.hash_mismatch")]
    [InlineData("runtime_path", "target.runtime_path")]
    [InlineData("overwrite", "target.overwrite")]
    [InlineData("missing_meta", "target.meta_missing")]
    public async Task CliRejectsUnsafeCx511Mutations(string mutation, string expectedCode)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchor-adoption.cx511.json")))!.AsObject();
        switch (mutation)
        {
            case "wrong_source_hash":
                root["sourceArtifact"]!["sha256"] = new string('0', 64);
                break;
            case "runtime_path":
                root["targetPath"] = "unity/RedMistVerticalSlice/Assets/StreamingAssets/identity_celadon_scout_v3.png";
                break;
            case "overwrite":
                root["overwriteExistingAssets"] = true;
                break;
            case "missing_meta":
                root["targetMetaPath"] = "unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/missing.png.meta";
                break;
        }

        var tempManifest = Path.Combine(Path.GetTempPath(), "cx511-mutated-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tempManifest, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "validate-visual-anchor-adoption",
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
