using System.Diagnostics;
using System.Text.Json;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace StoryValidator.Tests;

public sealed class StoryValidatorProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ValidFixtureReturnsZeroAndEmptyDiagnostics()
    {
        var result = await RunValidatorAsync("valid");

        Assert.Equal(0, result.ExitCode);
        using var output = ParseOutput(result);
        var root = output.RootElement;

        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task InvalidInputReactionResponseContributesItsNextNodeGraphEdge()
    {
        var result = await RunValidatorAsync("valid-invalid-input-edge");

        Assert.Equal(0, result.ExitCode);
        using var output = ParseOutput(result);
        Assert.True(output.RootElement.GetProperty("valid").GetBoolean());
        Assert.Empty(output.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task MissingBundleReturnsOneAndWritesOnlyStderr()
    {
        var result = await RunValidatorAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
    }

    [Theory]
    [InlineData("duplicate-id", "id.duplicate", "/sceneNodes/1/id")]
    [InlineData("dangling-reference", "reference.not_found", "/sceneNodes/0/choices/0/nextNodeId")]
    [InlineData("unreachable-node", "graph.node_unreachable", "/sceneNodes/1/id")]
    [InlineData("no-exit", "graph.node_no_exit", "/sceneNodes/0/choices")]
    [InlineData("missing-offline-fallback", "offline.fallback_missing", "/sceneNodes/0/offlineFallbackNodeId")]
    [InlineData("incomplete-quest-state", "quest.lifecycle_incomplete", "/questDefinitions/0/onExpire")]
    [InlineData("missing-media", "media.file_missing", "/mediaAssets/0/uri")]
    [InlineData("cycle-without-termination", "graph.cycle_without_termination", "/sceneNodes/0/id")]
    [InlineData("dangling-invalid-input-response", "reference.not_found", "/sceneNodes/0/invalid_input_rules/abuse/npcResponseRef")]
    public async Task InvalidFixtureReturnsTwoAndExpectedDiagnostic(
        string fixtureName,
        string expectedCode,
        string expectedPath)
    {
        var result = await RunValidatorAsync("invalid", fixtureName);

        Assert.Equal(2, result.ExitCode);
        using var output = ParseOutput(result);
        var root = output.RootElement;
        var diagnostics = root.GetProperty("diagnostics").EnumerateArray().ToArray();

        Assert.False(root.GetProperty("valid").GetBoolean());
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.GetProperty("code").GetString());
        Assert.Equal(expectedPath, diagnostic.GetProperty("path").GetString());
    }

    private static async Task<ProcessResult> RunValidatorAsync(params string[] fixtureSegments)
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = CurrentConfiguration();
        var validatorDll = Path.Combine(
            repositoryRoot.FullName,
            "tools",
            "StoryValidator",
            "bin",
            configuration,
            "net8.0",
            "StoryValidator.dll");
        var fixtureDirectory = Path.Combine(
            [
                repositoryRoot.FullName,
                "tests",
                "StoryValidatorFixtures",
                .. fixtureSegments,
            ]);

        Assert.True(File.Exists(validatorDll), $"StoryValidator output is missing: {validatorDll}");
        Assert.True(Directory.Exists(fixtureDirectory), $"Fixture directory is missing: {fixtureDirectory}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(validatorDll);
        startInfo.ArgumentList.Add(fixtureDirectory);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "StoryValidator process did not start.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"StoryValidator exceeded {ProcessTimeout.TotalSeconds} seconds.");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static JsonDocument ParseOutput(ProcessResult result)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(result.Stdout),
            $"StoryValidator did not emit JSON. stderr: {result.Stderr}");

        try
        {
            return JsonDocument.Parse(result.Stdout);
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"StoryValidator stdout is not JSON: {exception.Message}{Environment.NewLine}" +
                $"stdout: {result.Stdout}{Environment.NewLine}" +
                $"stderr: {result.Stderr}");
        }
    }

    private static string CurrentConfiguration()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        return frameworkDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the current build configuration.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory;
            }
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
