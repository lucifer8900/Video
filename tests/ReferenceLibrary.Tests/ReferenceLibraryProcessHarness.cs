using System.Diagnostics;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReferenceLibrary.Tests;

internal static class ReferenceLibraryProcessHarness
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    public static DirectoryInfo RepositoryRoot { get; } = FindRepositoryRoot();

    public static async Task<ProcessResult> ValidateAsync(string catalogPath, string downloadsRoot)
    {
        var toolDll = Path.Combine(
            RepositoryRoot.FullName,
            "tools",
            "ReferenceLibrary",
            "bin",
            CurrentConfiguration(),
            "net8.0",
            "ReferenceLibrary.dll");
        Assert.True(File.Exists(toolDll), $"Tool output is missing: {toolDll}");

        return await RunDotNetAsync(
            toolDll,
            "validate",
            "--catalog",
            catalogPath,
            "--downloads-root",
            downloadsRoot);
    }

    public static async Task<ProcessResult> RunGitAsync(params string[] arguments) =>
        await RunProcessAsync("git", RepositoryRoot.FullName, arguments);

    public static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "cx506-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<ProcessResult> RunDotNetAsync(string dll, params string[] arguments)
    {
        var allArguments = new[] { dll }.Concat(arguments).ToArray();
        return await RunProcessAsync("dotnet", RepositoryRoot.FullName, allArguments);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), $"Could not start {fileName}.");

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
            throw new TimeoutException($"{fileName} exceeded {ProcessTimeout.TotalSeconds} seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string CurrentConfiguration()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return frameworkDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine test configuration.");
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
