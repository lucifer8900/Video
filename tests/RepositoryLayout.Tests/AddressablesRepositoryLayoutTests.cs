using System.Text.Json;

namespace RepositoryLayout.Tests;

public sealed class AddressablesRepositoryLayoutTests
{
    [Fact]
    public void AddressablesExperimentScaffoldIsPinnedVersionedAndPresent()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string[] requiredPaths =
        [
            "content/packaging/cx504-addressables-pack-plan.json",
            "server/Contracts/schemas/addressables-pack-plan.schema.json",
            "server/Contracts/schemas/addressables-patch-report.schema.json",
            "unity/RedMistVerticalSlice/Assets/Editor/Cx504/AddressablesContentPackBuilder.cs",
            "unity/RedMistVerticalSlice/Assets/Editor/Cx504/IncrementalPatchExperiment.cs",
            "unity/RedMistVerticalSlice/Assets/Tests/EditMode/Cx504/AddressablesContentPackTests.cs",
            "unity/RedMistVerticalSlice/Docs/CX504-Addressables-Experiment.md",
        ];
        string[] missing = requiredPaths
            .Where(path => !File.Exists(Path.Combine(root.FullName, Platform(path))))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"CX-504 files are missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");

        string manifestPath = Path.Combine(
            root.FullName,
            Platform("unity/RedMistVerticalSlice/Packages/manifest.json"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(
            "1.22.3",
            manifest.RootElement.GetProperty("dependencies")
                .GetProperty("com.unity.addressables")
                .GetString());

        string ignore = File.ReadAllText(Path.Combine(root.FullName, ".gitignore"))
            .Replace('\\', '/');
        Assert.Contains(
            "/unity/RedMistVerticalSlice/Assets/Cx504Generated/",
            ignore,
            StringComparison.Ordinal);
        Assert.Contains(
            "/unity/RedMistVerticalSlice/Assets/AddressableAssetsData/",
            ignore,
            StringComparison.Ordinal);
        Assert.Contains(
            "/unity/RedMistVerticalSlice/ServerData/",
            ignore,
            StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory;
        }
        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }

    private static string Platform(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
