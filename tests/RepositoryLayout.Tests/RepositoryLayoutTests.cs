namespace RepositoryLayout.Tests;

public sealed class RepositoryLayoutTests
{
    private static readonly string[] ProductionProjectPaths =
    [
        "server/Api/Api.csproj",
        "server/Worker/Worker.csproj",
        "server/Contracts/Contracts.csproj",
        "tools/NarrativeCompiler/NarrativeCompiler.csproj",
        "tools/StoryValidator/StoryValidator.csproj",
        "tools/VoiceEvaluator/VoiceEvaluator.csproj",
    ];

    private static readonly string[] SolutionProjectPaths =
    [
        .. ProductionProjectPaths,
        "tests/RepositoryLayout.Tests/RepositoryLayout.Tests.csproj",
        "tests/VoiceEvaluator.Tests/VoiceEvaluator.Tests.csproj",
    ];

    [Fact]
    public void RequiredRepositoryFilesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        string[] requiredPaths =
        [
            "Video.sln",
            .. ProductionProjectPaths,
            "tests/VoiceEvaluator.Tests/VoiceEvaluator.Tests.csproj",
            "tests/VoiceFixtures/README.md",
            "tests/VoiceFixtures/voice-evaluation.dataset.json",
            "server/Contracts/schemas/voice-evaluation-report.schema.json",
            ".github/workflows/dotnet.yml",
        ];

        var missingPaths = requiredPaths
            .Where(path => !File.Exists(Path.Combine(repositoryRoot.FullName, ToPlatformPath(path))))
            .ToArray();

        Assert.True(
            missingPaths.Length == 0,
            $"Required repository files are missing:{Environment.NewLine}{string.Join(Environment.NewLine, missingPaths)}");
    }

    [Fact]
    public void SolutionReferencesEveryRequiredProject()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot.FullName, "Video.sln");
        if (!File.Exists(solutionPath))
        {
            return;
        }

        var solutionText = File.ReadAllText(solutionPath).Replace('\\', '/');

        foreach (var projectPath in SolutionProjectPaths)
        {
            Assert.Contains(projectPath, solutionText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProductionProjectsTargetDotNetEight()
    {
        var repositoryRoot = FindRepositoryRoot();

        foreach (var projectPath in ProductionProjectPaths)
        {
            var fullPath = Path.Combine(repositoryRoot.FullName, ToPlatformPath(projectPath));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var projectText = File.ReadAllText(fullPath);
            Assert.Contains("<TargetFramework>net8.0</TargetFramework>", projectText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkflowBuildsAndTestsOnPushWithDotNetEight()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot.FullName, ToPlatformPath(".github/workflows/dotnet.yml"));
        if (!File.Exists(workflowPath))
        {
            return;
        }

        var workflowText = File.ReadAllText(workflowPath);

        Assert.Contains("on:", workflowText, StringComparison.Ordinal);
        Assert.Contains("push:", workflowText, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: '8.0.x'", workflowText, StringComparison.Ordinal);
        Assert.Contains("dotnet build Video.sln", workflowText, StringComparison.Ordinal);
        Assert.Contains("dotnet test Video.sln", workflowText, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not find the Video repository root from the test output directory.");
    }

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
