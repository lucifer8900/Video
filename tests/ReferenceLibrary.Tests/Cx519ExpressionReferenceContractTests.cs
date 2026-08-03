using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceLibrary.Tests;

public sealed class Cx519ExpressionReferenceContractTests
{
    private static readonly string[] ExpectedSubjects =
    [
        "character.shen_yan",
        "character.chu_mingqi",
        "character.shi_jun",
        "character.celadon_scout",
        "character.cavern_ally",
        "character.formation_spirit",
    ];

    private static readonly string[] ExpectedEmotions =
    [
        "neutral",
        "calm_warmth",
        "restrained_joy",
        "resolve",
        "vigilance",
        "suspicion",
        "concern",
        "grief",
        "anger_controlled",
        "fear_suppressed",
        "pain",
        "astonishment",
    ];

    [Fact]
    public async Task CheckedInCx519ManifestPassesStrictValidation()
    {
        var result = await RunValidatorAsync(ManifestPath());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "CX-519 expression references valid: 6 subjects, 18 boards, 72 independent expressions, all pending human review",
            result.Stdout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CheckedInManifestPinsSixSubjectsAndTwelveOrderedEmotions()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var root = manifest.RootElement;
        Assert.Equal("CX-519", root.GetProperty("cardId").GetString());
        Assert.Equal(ExpectedEmotions, root.GetProperty("emotionOrder").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(ExpectedSubjects, root.GetProperty("subjects").EnumerateArray().Select(item => item.GetProperty("subjectId").GetString()).ToArray());
        Assert.All(root.GetProperty("subjects").EnumerateArray(), subject =>
        {
            Assert.Equal(3, subject.GetProperty("boards").GetArrayLength());
            Assert.Equal(12, subject.GetProperty("expressions").GetArrayLength());
        });
    }

    [Fact]
    public void CheckedInManifestRecordsImageOnlyReviewBoundary()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var policy = manifest.RootElement.GetProperty("generationPolicy");
        Assert.Equal("codex_builtin_imagegen", policy.GetProperty("imageProvider").GetString());
        Assert.Equal(24, policy.GetProperty("generationCalls").GetInt32());
        Assert.Equal(19, policy.GetProperty("successfulOutputs").GetInt32());
        Assert.Equal(5, policy.GetProperty("failedCalls").GetInt32());
        Assert.Equal(1, policy.GetProperty("discardedOutputs").GetInt32());
        Assert.Equal(18, policy.GetProperty("compositeBoardFiles").GetInt32());
        Assert.Equal(72, policy.GetProperty("independentExpressionFiles").GetInt32());
        Assert.False(policy.GetProperty("runtimeUse").GetBoolean());
        Assert.False(policy.GetProperty("shipInBuild").GetBoolean());
        Assert.False(policy.GetProperty("videoOrAudioUsed").GetBoolean());
        Assert.Equal("paused", policy.GetProperty("cx514Status").GetString());
    }

    [Fact]
    public void CheckedInManifestPinsApprovedCx518Source()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var source = manifest.RootElement.GetProperty("sourceAdoption");
        Assert.Equal("CX-518", source.GetProperty("cardId").GetString());
        Assert.Equal("content/story/red-mist/red-mist-multiview-adoption.cx518.json", source.GetProperty("path").GetString());
        Assert.True(source.GetProperty("expressionGenerationAllowed").GetBoolean());
        Assert.Matches("^[0-9a-f]{64}$", source.GetProperty("sha256").GetString()!);
    }

    [Theory]
    [InlineData("source_hash", "source.adoption_hash")]
    [InlineData("provider", "policy.imagegen")]
    [InlineData("missing_subject", "coverage.subjects")]
    [InlineData("wrong_subject_order", "coverage.subjects")]
    [InlineData("missing_emotion", "coverage.emotions")]
    [InlineData("wrong_emotion_order", "coverage.emotions")]
    [InlineData("wrong_path", "artifact.path")]
    [InlineData("artifact_hash", "artifact.file_hash")]
    [InlineData("artifact_size", "artifact.file_metadata")]
    [InlineData("media_type", "artifact.png_signature")]
    [InlineData("adopted", "adoption.pending_review")]
    [InlineData("runtime", "adoption.runtime")]
    [InlineData("video", "policy.no_video_audio")]
    public async Task CliRejectsUnsafeCx519Mutations(string mutation, string expectedCode)
    {
        var fixtureRoot = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath()))!.AsObject();
            ApplyMutation(manifest, mutation);
            var path = Path.Combine(fixtureRoot, "red-mist-expression-references.cx519.json");
            await File.WriteAllTextAsync(path, manifest.ToJsonString());

            var result = await RunValidatorAsync(path);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedCode, result.Stdout + result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static void ApplyMutation(JsonObject manifest, string mutation)
    {
        var subjects = manifest["subjects"]!.AsArray();
        var expressions = subjects[0]!["expressions"]!.AsArray();
        switch (mutation)
        {
            case "source_hash": manifest["sourceAdoption"]!["sha256"] = new string('0', 64); break;
            case "provider": manifest["generationPolicy"]!["imageProvider"] = "other"; break;
            case "missing_subject": subjects.RemoveAt(0); break;
            case "wrong_subject_order":
                var firstSubject = subjects[0]!.DeepClone();
                subjects[0] = subjects[1]!.DeepClone();
                subjects[1] = firstSubject;
                break;
            case "missing_emotion": expressions.RemoveAt(0); break;
            case "wrong_emotion_order":
                var firstEmotion = expressions[0]!.DeepClone();
                expressions[0] = expressions[1]!.DeepClone();
                expressions[1] = firstEmotion;
                break;
            case "wrong_path": expressions[0]!["outputPath"] = "content/visual-candidates/cx519/expressions/wrong_v1.png"; break;
            case "artifact_hash": expressions[0]!["artifact"]!["sha256"] = new string('0', 64); break;
            case "artifact_size": expressions[0]!["artifact"]!["byteLength"] = expressions[0]!["artifact"]!["byteLength"]!.GetValue<long>() + 1; break;
            case "media_type": expressions[0]!["artifact"]!["mediaType"] = "image/jpeg"; break;
            case "adopted": expressions[0]!["adopted"] = true; break;
            case "runtime": expressions[0]!["runtimeUse"] = true; break;
            case "video": manifest["generationPolicy"]!["videoOrAudioUsed"] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static async Task<ProcessResult> RunValidatorAsync(string manifestPath) =>
        await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-expression-references",
            "--manifest", manifestPath,
            "--source-adoption", ProjectPath("content/story/red-mist/red-mist-multiview-adoption.cx518.json"),
            "--asset-root", ProjectPath("content/visual-candidates/cx519"));

    private static string ManifestPath() => ProjectPath("content/story/red-mist/red-mist-expression-references.cx519.json");

    private static string ProjectPath(params string[] segments) =>
        Path.Combine([ReferenceLibraryProcessHarness.RepositoryRoot.FullName, .. segments]);
}
