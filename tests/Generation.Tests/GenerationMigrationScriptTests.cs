using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationMigrationScriptTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/001_generation_jobs.sql";

    [Fact]
    public void InitialMigrationContainsDatabaseEnforcedStateAndIdempotencyGuards()
    {
        string sql = ReadMigration();

        AssertSqlContains(sql, "create table", "generation_jobs");
        AssertSqlContains(sql, "idempotency_key", "input_hash", "unique");
        AssertSqlContains(sql, "status", "check");
        AssertSqlContains(
            sql,
            "created",
            "queued",
            "generating",
            "moderating",
            "transcoding",
            "ready",
            "failed",
            "expired");
        AssertSqlContains(sql, "create trigger", "old.status", "new.status");
        AssertSqlContains(sql, "before insert", "new.status", "created");
    }

    [Fact]
    public void InitialMigrationPersistsOptimisticConcurrencyAndRecoverableLeases()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "version",
            "lease_owner",
            "lease_expires_at_utc",
            "attempt_count");
        Assert.Matches(
            new Regex(
                @"create\s+(unique\s+)?index[\s\S]+generation_jobs[\s\S]+status[\s\S]+lease_expires_at_utc",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    private static string ReadMigration()
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(
            repositoryRoot,
            RelativeMigrationPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing CX-302 migration: {path}");
        return File.ReadAllText(path);
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

    private static void AssertSqlContains(string sql, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
    }
}
