namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationFailureMigrationScriptTests
{
    private const string RelativePath =
        "server/Generation/Persistence/Migrations/003_generation_failure_codes.sql";

    [Fact]
    public void MigrationPersistsOnlySanitizedTerminalFailureCodes()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing CX-306 migration: {path}");

        string sql = File.ReadAllText(path).ToLowerInvariant();
        Assert.Contains("failure_code", sql, StringComparison.Ordinal);
        Assert.Contains("generation.failed", sql, StringComparison.Ordinal);
        Assert.Contains("moderation.rejected", sql, StringComparison.Ordinal);
        Assert.Contains("check", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", sql, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Video.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Video repository root.");
    }
}
