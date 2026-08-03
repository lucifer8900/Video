namespace Lingmai.RedMist.Generation.Tests;

public sealed class PostgresBudgetArchitectureTests
{
    [Fact]
    public void RepositorySerializesAbsentIdempotencyAndEventKeysAcrossTransactions()
    {
        string source = ReadRepositorySource();

        Assert.Contains("pg_advisory_xact_lock", source, StringComparison.Ordinal);
        Assert.Contains("LockTransactionKeyAsync", source, StringComparison.Ordinal);
        Assert.Contains("budget-reservation", source, StringComparison.Ordinal);
        Assert.Contains("budget-event", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryUsesTrustedTimeAndDatabaseResolvedDailyPeriod()
    {
        string source = ReadRepositorySource();

        Assert.Contains("TimeProvider", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentDailyScopeAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetUtcNow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryUsesOneStableScopeOrderAndFailsClosedOnPartialCounters()
    {
        string source = ReadRepositorySource();

        Assert.Contains("OrderScopes", source, StringComparison.Ordinal);
        Assert.Contains("result.Count != ScopeKinds.Length", source, StringComparison.Ordinal);
    }

    private static string ReadRepositorySource()
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "server",
            "Generation",
            "Persistence",
            "PostgresBudgetRepository.cs"));
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
