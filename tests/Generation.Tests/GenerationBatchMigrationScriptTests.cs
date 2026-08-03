using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationBatchMigrationScriptTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/007_generation_batches.sql";

    [Fact]
    public void MigrationCreatesDurableBatchItemAttemptAndReviewLedger()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "create table generation_batches",
            "create table generation_batch_items",
            "create table generation_batch_attempts",
            "create table generation_batch_reviews",
            "idempotency_key",
            "request_fingerprint",
            "manifest_content_hash",
            "source_preview_batch_id",
            "source_preview_shot_id",
            "generation_job_id",
            "actual_duration_milliseconds",
            "actual_cost_micros",
            "cost_settled",
            "dialogue_binding",
            "first_frame",
            "last_frame",
            "primary_media",
            "fallback_media",
            "expected_attempt_number",
            "expected_artifact_hash",
            "run_lease_owner",
            "run_lease_expires_at_utc",
            "run_lease_fencing_token");
    }

    [Fact]
    public void MigrationEnforcesTwentyShotLimitUniquenessAndOneOpenBatch()
    {
        string sql = ReadMigration();

        Assert.Matches(
            new Regex(
                @"item_count\s+integer\s+not\s+null[\s\S]*check\s*\(\s*item_count\s+between\s+1\s+and\s+20\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
        AssertSqlContains(
            sql,
            "unique",
            "idempotency_key",
            "unique (batch_id, shot_id)",
            "unique (batch_id, shot_id, attempt_number)",
            "unique (source_preview_batch_id, source_preview_shot_id)",
            "where status in ('running', 'awaiting_review')");
    }

    [Fact]
    public void MigrationRequiresCompleteApprovedPinnedGenerationSpecs()
    {
        string sql = ReadMigration();

        foreach (string column in new[] { "first_frame", "last_frame", "primary_media", "fallback_media" })
        {
            Assert.Matches(
                new Regex(
                    $@"{column}\s+jsonb\s+not\s+null",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                sql);
        }
        AssertSqlContains(
            sql,
            "ck_generation_batch_items_pin_objects",
            "approved_frame",
            "bound_to_primary_media",
            "'video', 'animation'",
            "'video', 'animation', 'image'");
    }

    [Fact]
    public void MigrationStoresSanitizedAuditDataOnly()
    {
        string sql = ReadMigration();

        Assert.DoesNotContain("raw_prompt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider_response", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationPinsPromotionsAndReviewsToApprovedSourceAttempts()
    {
        string sql = ReadMigration();

        AssertSqlContains(
            sql,
            "enforce_generation_batch_promotion_source",
            "preview_fast",
            "reviewed",
            "adopted",
            "uq_generation_batch_attempt_review_pin",
            "expected_attempt_number",
            "expected_artifact_hash",
            "fk_generation_batch_review_items_attempt");
    }

    private static string ReadMigration()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Video.sln")))
            current = current.Parent;
        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");

        string path = Path.Combine(
            current.FullName,
            RelativeMigrationPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing CX-502 migration: {path}");
        return File.ReadAllText(path);
    }

    private static void AssertSqlContains(string sql, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
    }
}
