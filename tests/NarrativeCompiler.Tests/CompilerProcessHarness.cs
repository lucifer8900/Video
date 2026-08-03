using System.Diagnostics;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NarrativeCompiler.Tests;

internal static class CompilerProcessHarness
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    public static DirectoryInfo RepositoryRoot { get; } = FindRepositoryRoot();

    public static string SourceDirectory => Path.Combine(
        RepositoryRoot.FullName,
        "content",
        "story",
        "red-mist");

    public static async Task<ProcessResult> RunCompilerAsync(
        string outputDirectory,
        string? sourceDirectory = null)
    {
        var compilerDll = ToolDll("NarrativeCompiler");
        return await RunDotNetAsync(
            compilerDll,
            sourceDirectory ?? SourceDirectory,
            outputDirectory);
    }

    public static async Task<ProcessResult> RunShotManifestGeneratorAsync(
        string outputDirectory,
        string? bundlePath = null)
    {
        var compilerDll = ToolDll("NarrativeCompiler");
        return await RunDotNetAsync(
            compilerDll,
            "shot-manifest",
            bundlePath ?? Path.Combine(SourceDirectory, "story.bundle.json"),
            outputDirectory);
    }

    public static async Task<ProcessResult> RunValidatorAsync(string bundleDirectory)
    {
        var validatorDll = ToolDll("StoryValidator");
        return await RunDotNetAsync(validatorDll, bundleDirectory);
    }

    public static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "cx103-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<ProcessResult> RunDotNetAsync(string dll, params string[] arguments)
    {
        Assert.True(File.Exists(dll), $"Tool output is missing: {dll}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(dll);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), $"Could not start {Path.GetFileName(dll)}.");

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
            throw new TimeoutException($"{Path.GetFileName(dll)} exceeded {ProcessTimeout.TotalSeconds} seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string ToolDll(string projectName) => Path.Combine(
        RepositoryRoot.FullName,
        "tools",
        projectName,
        "bin",
        CurrentConfiguration(),
        "net8.0",
        projectName + ".dll");

    private static string CurrentConfiguration()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        return frameworkDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the test build configuration.");
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
}

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
