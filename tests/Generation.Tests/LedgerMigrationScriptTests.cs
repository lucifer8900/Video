namespace Lingmai.RedMist.Generation.Tests;

public sealed class LedgerMigrationScriptTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/004_ledger_events.sql";
    private const string DurabilityMigrationPath =
        "server/Generation/Persistence/Migrations/005_ledger_ingest_sequence_and_tombstones.sql";

    [Fact]
    public void LedgerMigrationEnforcesCompositeIdempotencyAndBoundedQueryIndex()
    {
        string sql = (ReadMigration(RelativeMigrationPath) + "\n" +
                      ReadMigration(DurabilityMigrationPath)).ToLowerInvariant();

        Assert.Contains("primary key (player_id, entry_id)", sql, StringComparison.Ordinal);
        Assert.Contains("content_fingerprint", sql, StringComparison.Ordinal);
        Assert.Contains("current_timestamp", sql, StringComparison.Ordinal);
        Assert.Contains("generated always as identity", sql, StringComparison.Ordinal);
        Assert.Contains("player_id, chapter, ingest_sequence", sql, StringComparison.Ordinal);
        Assert.Contains("ledger_player_tombstones", sql, StringComparison.Ordinal);
        Assert.Contains("jsonb_typeof(payload) = 'object'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepositoryUsesAtomicTrustedTimeParameterizedOperations()
    {
        string source = ReadRepository();

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (player_id, entry_id) DO NOTHING", source, StringComparison.Ordinal);
        Assert.Contains("CURRENT_TIMESTAMP", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY ingest_sequence ASC", source, StringComparison.Ordinal);
        Assert.Contains("LIMIT @take", source, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO ledger_player_tombstones", source, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM narrative_ledger_events WHERE player_id = @player_id", source, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.Jsonb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("receivedAtUtc.ToString", source, StringComparison.Ordinal);
    }

    private static string ReadMigration(string relativePath)
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Migration is missing: {path}");
        return File.ReadAllText(path);
    }

    private static string ReadRepository()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "server",
            "Generation",
            "Persistence",
            "PostgresLedgerRepository.cs");
        Assert.True(File.Exists(path), $"Repository is missing: {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }
}
