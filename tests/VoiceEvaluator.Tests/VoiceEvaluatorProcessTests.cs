using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VoiceEvaluator.Tests;

public sealed class VoiceEvaluatorProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ValidFixtureProducesPassingCompleteSchemaValidReport()
    {
        DirectoryInfo repositoryRoot = TestRepository.FindRoot();
        string outputDirectory = TestRepository.CreateTempDirectory("cx205-report-");

        try
        {
            ProcessResult result = await RunEvaluatorAsync(repositoryRoot, outputDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.Stderr), result.Stderr);

            string jsonPath = Path.Combine(outputDirectory, "voice-evaluation.report.json");
            string markdownPath = Path.Combine(outputDirectory, "voice-evaluation.report.md");
            Assert.True(File.Exists(jsonPath), "The JSON report was not written.");
            Assert.True(File.Exists(markdownPath), "The Markdown report was not written.");

            string reportText = File.ReadAllText(jsonPath);
            Assert.Equal(reportText.Trim(), result.Stdout.Trim());
            Assert.DoesNotContain("\"transcript\"", reportText, StringComparison.Ordinal);
            Assert.DoesNotContain("audioRef", reportText, StringComparison.OrdinalIgnoreCase);

            using JsonDocument report = JsonDocument.Parse(reportText);
            JsonElement root = report.RootElement;
            Assert.Equal("synthetic_text_regression", root.GetProperty("evaluationScope").GetString());
            Assert.Equal("not_evaluable_no_production_voice_intents", root.GetProperty("productionReadiness").GetString());
            Assert.Equal("local_keyword", root.GetProperty("classifierId").GetString());
            Assert.Equal(200, root.GetProperty("sampleCount").GetInt32());
            Assert.Equal(0, root.GetProperty("audioSampleCount").GetInt32());
            Assert.Equal(200, root.GetProperty("samples").GetArrayLength());
            Assert.All(
                root.GetProperty("samples").EnumerateArray(),
                sample =>
                {
                    Assert.True(sample.TryGetProperty("expectedOutcome", out _));
                    Assert.True(sample.TryGetProperty("expectedIntentId", out _));
                    Assert.True(sample.TryGetProperty("actualOutcome", out _));
                    Assert.True(sample.TryGetProperty("actualIntentId", out _));
                    Assert.InRange(sample.GetProperty("confidence").GetDouble(), 0d, 1d);
                    Assert.Contains(
                        sample.GetProperty("source").GetString(),
                        new[] { "detector", "local_keyword" });
                    if (sample.GetProperty("actualOutcome").GetString() != "matched")
                        Assert.Equal(JsonValueKind.Null, sample.GetProperty("actualIntentId").ValueKind);
                });

            JsonElement metrics = root.GetProperty("metrics");
            Assert.True(metrics.GetProperty("validIntentAccuracy").GetDouble() >= 0.9d);
            Assert.True(metrics.GetProperty("severeFalsePositiveRate").GetDouble() < 0.01d);
            Assert.Equal(0, metrics.GetProperty("severeFalsePositiveCount").GetInt32());
            Assert.True(root.GetProperty("passed").GetBoolean());

            AssertSchemaValid(repositoryRoot, report.RootElement);
            Assert.Contains("不代表生产语音已就绪", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedRunProducesByteIdenticalJsonAndMarkdown()
    {
        DirectoryInfo repositoryRoot = TestRepository.FindRoot();
        string firstDirectory = TestRepository.CreateTempDirectory("cx205-repeat-a-");
        string secondDirectory = TestRepository.CreateTempDirectory("cx205-repeat-b-");

        try
        {
            ProcessResult first = await RunEvaluatorAsync(repositoryRoot, firstDirectory);
            ProcessResult second = await RunEvaluatorAsync(repositoryRoot, secondDirectory);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory, "voice-evaluation.report.json")),
                File.ReadAllBytes(Path.Combine(secondDirectory, "voice-evaluation.report.json")));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory, "voice-evaluation.report.md")),
                File.ReadAllBytes(Path.Combine(secondDirectory, "voice-evaluation.report.md")));
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DatasetWithOnlyOneHundredNinetyNineSamplesReturnsOneAndWritesNoReport()
    {
        DirectoryInfo repositoryRoot = TestRepository.FindRoot();
        string temporaryDirectory = TestRepository.CreateTempDirectory("cx205-invalid-");
        string outputDirectory = Path.Combine(temporaryDirectory, "output");
        string sourcePath = DatasetPath(repositoryRoot);
        string invalidPath = Path.Combine(temporaryDirectory, "invalid.dataset.json");

        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
            root["samples"]!.AsArray().RemoveAt(199);
            File.WriteAllText(invalidPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            ProcessResult result = await RunEvaluatorAsync(
                repositoryRoot,
                outputDirectory,
                invalidPath);

            Assert.Equal(1, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.Stdout));
            Assert.Contains("/samples", result.Stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "voice-evaluation.report.json")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "voice-evaluation.report.md")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunEvaluatorAsync(
        DirectoryInfo repositoryRoot,
        string outputDirectory,
        string? datasetPath = null)
    {
        string configuration = TestRepository.CurrentConfiguration();
        string evaluatorDll = Path.Combine(
            repositoryRoot.FullName,
            "tools",
            "VoiceEvaluator",
            "bin",
            configuration,
            "net8.0",
            "VoiceEvaluator.dll");
        string fixturePath = datasetPath ?? DatasetPath(repositoryRoot);

        Assert.True(File.Exists(evaluatorDll), $"VoiceEvaluator output is missing: {evaluatorDll}");
        Assert.True(File.Exists(fixturePath), $"Voice fixture is missing: {fixturePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(evaluatorDll);
        startInfo.ArgumentList.Add(fixturePath);
        startInfo.ArgumentList.Add(outputDirectory);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "VoiceEvaluator process did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"VoiceEvaluator exceeded {ProcessTimeout.TotalSeconds} seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string DatasetPath(DirectoryInfo repositoryRoot) => Path.Combine(
        repositoryRoot.FullName,
        "tests",
        "VoiceFixtures",
        "voice-evaluation.dataset.json");

    private static void AssertSchemaValid(DirectoryInfo repositoryRoot, JsonElement report)
    {
        string schemaPath = Path.Combine(
            repositoryRoot.FullName,
            "server",
            "Contracts",
            "schemas",
            "voice-evaluation-report.schema.json");
        Assert.True(File.Exists(schemaPath), $"Report schema is missing: {schemaPath}");

        string commonPath = Path.Combine(
            repositoryRoot.FullName,
            "server",
            "Contracts",
            "schemas",
            "common.schema.json");
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        using JsonDocument commonDocument = JsonDocument.Parse(File.ReadAllText(commonPath));
        _ = JsonSchema.Build(commonDocument.RootElement.Clone(), buildOptions);
        using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath));
        JsonSchema schema = JsonSchema.Build(schemaDocument.RootElement.Clone(), buildOptions);
        EvaluationResults evaluation = schema.Evaluate(
            report,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
        Assert.True(evaluation.IsValid, "The generated report did not satisfy voice-evaluation-report.schema.json.");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}

internal static class TestRepository
{
    public static DirectoryInfo FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) return directory;
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }

    public static string CreateTempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CurrentConfiguration()
    {
        string[] segments = AppContext.BaseDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("Release", StringComparer.OrdinalIgnoreCase) ? "Release" : "Debug";
    }
}
