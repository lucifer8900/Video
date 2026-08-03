using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class StoryBundlePathResolverTests
{
    [Fact]
    public void MissingWorkingDirectoryCopyFallsBackToApplicationBase()
    {
        string root = Path.Combine(Path.GetTempPath(), "cx204-bundle-path-" + Guid.NewGuid().ToString("N"));
        string workingDirectory = Path.Combine(root, "working");
        string applicationBase = Path.Combine(root, "application");
        string relativePath = Path.Combine("content", "story", "red-mist", "story.bundle.json");
        string expected = Path.Combine(applicationBase, relativePath);

        try
        {
            Directory.CreateDirectory(workingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllText(expected, "{}");

            string actual = StoryBundlePathResolver.Resolve(
                relativePath,
                workingDirectory,
                applicationBase);

            Assert.Equal(Path.GetFullPath(expected), actual);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExistingWorkingDirectoryCopyTakesPriority()
    {
        string root = Path.Combine(Path.GetTempPath(), "cx204-bundle-path-" + Guid.NewGuid().ToString("N"));
        string workingDirectory = Path.Combine(root, "working");
        string applicationBase = Path.Combine(root, "application");
        const string relativePath = "story.bundle.json";
        string expected = Path.Combine(workingDirectory, relativePath);

        try
        {
            Directory.CreateDirectory(workingDirectory);
            Directory.CreateDirectory(applicationBase);
            File.WriteAllText(expected, "working");
            File.WriteAllText(Path.Combine(applicationBase, relativePath), "application");

            string actual = StoryBundlePathResolver.Resolve(
                relativePath,
                workingDirectory,
                applicationBase);

            Assert.Equal(Path.GetFullPath(expected), actual);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
