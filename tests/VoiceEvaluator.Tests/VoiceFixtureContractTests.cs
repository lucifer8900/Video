using System.Text.Json;

namespace VoiceEvaluator.Tests;

public sealed class VoiceFixtureContractTests
{
    [Fact]
    public void DatasetContainsExactlyTwoHundredUniqueTextOnlySamples()
    {
        DirectoryInfo repositoryRoot = TestRepository.FindRoot();
        string fixturePath = Path.Combine(
            repositoryRoot.FullName,
            "tests",
            "VoiceFixtures",
            "voice-evaluation.dataset.json");

        Assert.True(File.Exists(fixturePath), $"CX-205 fixture is missing: {fixturePath}");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        JsonElement root = document.RootElement;
        JsonElement[] samples = root.GetProperty("samples").EnumerateArray().ToArray();

        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("synthetic_text_regression", root.GetProperty("scope").GetString());
        Assert.True(root.GetProperty("testOnly").GetBoolean());
        Assert.True(root.GetProperty("doNotShip").GetBoolean());
        Assert.Equal(200, samples.Length);
        Assert.Equal(200, samples.Select(sample => sample.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());

        var counts = samples
            .GroupBy(sample => sample.GetProperty("expectedOutcome").GetString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key!, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(100, counts["matched"]);
        Assert.Equal(30, counts["irrelevant"]);
        Assert.Equal(20, counts["abuse"]);
        Assert.Equal(15, counts["too_long"]);
        Assert.Equal(10, counts["silence"]);
        Assert.Equal(25, counts["low_confidence"]);

        string fixtureDirectory = Path.GetDirectoryName(fixturePath)!;
        string[] forbiddenAudio = Directory
            .EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories)
            .Where(path => new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(forbiddenAudio);
    }
}
