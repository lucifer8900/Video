using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationAuditMigrationScriptTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/006_association_generation_audits.sql";

    [Fact]
    public void MigrationReusesGenerationJobsAndCreatesStrictOneToOneAuditDetails()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "alter table generation_jobs",
            "workload_kind",
            "association_decision",
            "generation_job_association_audits",
            "generation_job_id",
            "references generation_jobs",
            "on delete cascade",
            "audit_ref",
            "thread_id",
            "unique");
        Assert.Matches(
            new Regex(
                @"generation_job_id\s+uuid\s+primary\s+key",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    [Fact]
    public void MigrationConstrainsJsonPrivacyFallbackAndAssociationTerminalState()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "resolved_params",
            "providers",
            "guard_results",
            "jsonb_typeof",
            "octet_length",
            "player_ref_hash",
            "candidate_count",
            "fallback_used",
            "outcome_reason",
            "latency_ms",
            "status",
            "ready");
        Assert.DoesNotContain("raw_prompt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider_response", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("injection_text", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationKeepsMediaGenerationInitialStatusGuardIntact()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "media_generation",
            "created",
            "association_decision",
            "ready",
            "enforce_generation_job_initial_status");
    }

    private static string ReadMigration()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "Video.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        string path = Path.Combine(
            current.FullName,
            RelativeMigrationPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing CX-406 migration: {path}");
        return File.ReadAllText(path);
    }

    private static void AssertSqlContains(string sql, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
    }
}
