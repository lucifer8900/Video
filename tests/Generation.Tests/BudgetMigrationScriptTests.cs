using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetMigrationScriptTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/002_generation_budgets.sql";

    [Fact]
    public void MigrationDefinesFiveDimensionLimitsAndNonnegativeLedger()
    {
        string sql = ReadMigration();

        AssertContains(
            sql,
            "create table",
            "budget_limits",
            "budget_reservations",
            "budget_reservation_scopes",
            "budget_events",
            "account",
            "device",
            "chapter",
            "daily",
            "project",
            "check",
            ">= 0");
        Assert.Matches(
            new Regex(
                @"unique\s*\([^)]*idempotency_key[^)]*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    [Fact]
    public void MigrationEnforcesReservationStateAndEventIdempotency()
    {
        string sql = ReadMigration();

        AssertContains(
            sql,
            "reserved",
            "dispatching",
            "reconciliation_pending",
            "settled",
            "released",
            "denied",
            "create trigger",
            "old.status",
            "new.status",
            "event_id",
            "unique");
    }

    [Fact]
    public void ReservationFunctionLocksAllFiveScopesInStableOrder()
    {
        string sql = ReadMigration();

        AssertContains(sql, "create function", "for update", "scope_type", "scope_key");
        Assert.Matches(
            new Regex(
                @"order\s+by\s+case\s+scope_type" +
                @"\s+when\s+'account'\s+then\s+1" +
                @"\s+when\s+'device'\s+then\s+2" +
                @"\s+when\s+'chapter'\s+then\s+3" +
                @"\s+when\s+'daily'\s+then\s+4" +
                @"\s+when\s+'project'\s+then\s+5" +
                @"\s+end\s*,\s*scope_key[\s\S]*for\s+update",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
        Assert.Matches(
            new Regex(
                @"count\s*\([^)]*\)[\s\S]*(<>|!=)\s*5",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    [Fact]
    public void MigrationConstrainsEstimateAndReservationActualShapes()
    {
        string sql = CollapseWhitespace(ReadMigration());

        AssertContains(
            sql,
            "estimated_micros <= maximum_micros",
            "status = 'settled' AND actual_micros IS NOT NULL",
            "status IN ('released', 'denied') AND actual_micros = 0",
            "status IN ('reserved', 'dispatching', 'reconciliation_pending') AND actual_micros IS NULL");
    }

    [Fact]
    public void MigrationPersistsNonblankCostAuditMetadata()
    {
        string sql = CollapseWhitespace(ReadMigration());

        AssertContains(
            sql,
            "price_version text NOT NULL",
            "provider_key text NOT NULL",
            "model_snapshot text NOT NULL",
            "length(btrim(price_version)) > 0",
            "length(btrim(provider_key)) > 0",
            "length(btrim(model_snapshot)) > 0");
    }

    [Fact]
    public void MigrationConstrainsEventActualShape()
    {
        string sql = CollapseWhitespace(ReadMigration());

        AssertContains(
            sql,
            "event_type = 'settled' AND actual_micros IS NOT NULL",
            "event_type = 'released' AND actual_micros = 0",
            "event_type = 'reconciliation_pending' AND actual_micros IS NULL");
    }

    [Fact]
    public void MigrationPreventsOverlappingPeriodsForTheSameLimitIdentity()
    {
        string sql = ReadMigration();

        Assert.Matches(
            new Regex(
                @"unique\s*\(\s*scope_type\s*,\s*scope_key\s*,\s*currency\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    [Fact]
    public void MigrationDefersExactFiveScopeShapeValidationUntilCommit()
    {
        string sql = CollapseWhitespace(ReadMigration());

        AssertContains(
            sql,
            "create constraint trigger",
            "deferrable initially deferred",
            "count(distinct limits.scope_type)",
            "v_scope_count <> 5",
            "v_kind_count <> 5",
            "v_scope_count <> 0");
        Assert.Matches(
            new Regex(
                @"status\s*=\s*'denied'[\s\S]*v_scope_count\s*<>\s*0",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
    }

    private static string ReadMigration()
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(
            repositoryRoot,
            RelativeMigrationPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing CX-305 migration: {path}");
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

    private static void AssertContains(string sql, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();
}
