using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx509VisualAnchorContractTests
{
    private static readonly string[] RequiredAnchorIds =
    [
        "identity.chu_mingqi.v2",
        "identity.celadon_scout.v2",
        "identity.formation_spirit.v2",
        "identity.shi_jun.v3",
        "creature.ink_dragon.v3",
    ];

    [Fact]
    public async Task CheckedInCx509AnchorManifestPassesStrictValidation()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-visual-anchors",
            "--manifest",
            ProjectPath("content/story/red-mist/red-mist-visual-anchors.cx509.json"),
            "--catalog",
            ProjectPath("content/reference-library/catalog.json"),
            "--candidate-root",
            ProjectPath("content/visual-candidates/cx509"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CX-509 visual anchors valid: 5 anchors", result.Stdout);
    }

    [Fact]
    public void CheckedInManifestIsVersionedAndReviewOnly()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchors.cx509.json")));
        var root = manifest.RootElement;
        Assert.Equal("CX-509", root.GetProperty("cardId").GetString());
        Assert.True(root.GetProperty("generationPolicy").GetProperty("stillImagesOnly").GetBoolean());
        Assert.False(root.GetProperty("generationPolicy").GetProperty("geminiOrVeoUsed").GetBoolean());

        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(RequiredAnchorIds, candidates.Select(item => item.GetProperty("assetId").GetString()).ToArray());
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("needs_review", candidate.GetProperty("adoptionStatus").GetString());
            Assert.False(candidate.GetProperty("adopted").GetBoolean());
            Assert.False(candidate.GetProperty("shipInBuild").GetBoolean());
            Assert.StartsWith("content/visual-candidates/cx509/identity-anchors/", candidate.GetProperty("outputPath").GetString(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("adopted", "candidate.not_review_only")]
    [InlineData("runtime_path", "candidate.runtime_path")]
    [InlineData("wrong_version", "coverage.identity_anchors")]
    [InlineData("missing_role", "reference.role_missing")]
    [InlineData("missing_grand_rule", "candidate.continuity_contract")]
    public async Task CliRejectsUnsafeCx509Mutations(string mutation, string expectedCode)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProjectPath(
            "content/story/red-mist/red-mist-visual-anchors.cx509.json")))!.AsObject();
        var candidate = root["candidates"]![0]!.AsObject();
        switch (mutation)
        {
            case "adopted":
                candidate["adopted"] = true;
                break;
            case "runtime_path":
                candidate["outputPath"] = "unity/RedMistVerticalSlice/Assets/Resources/identity_chu_mingqi_v2.png";
                break;
            case "wrong_version":
                candidate["assetId"] = "identity.chu_mingqi.v1";
                break;
            case "missing_role":
                var references = candidate["referenceInputs"]!.AsArray();
                references.RemoveAt(0);
                break;
            case "missing_grand_rule":
                candidate["continuityLocks"]!.AsArray().RemoveAt(0);
                break;
        }

        var tempManifest = Path.Combine(Path.GetTempPath(), "cx509-mutated-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tempManifest, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "validate-visual-anchors",
                "--manifest",
                tempManifest,
                "--catalog",
                ProjectPath("content/reference-library/catalog.json"),
                "--candidate-root",
                ProjectPath("content/visual-candidates/cx509"));

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
