using Lingmai.RedMist.Api.Ledger;
using Microsoft.Extensions.Configuration;

namespace Lingmai.RedMist.Api.Tests;

public sealed class LedgerRuntimeOptionsTests
{
    [Fact]
    public void ExplicitInMemoryConfigurationIsAcceptedAsNonDurableLocalMode()
    {
        IConfiguration configuration = Configuration(
            ("Ledger:Enabled", "true"),
            ("Ledger:Provider", "InMemory"));

        LedgerRuntimeOptions options = LedgerRuntimeOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.False(options.UsePostgres);
        Assert.Null(options.ConnectionString);
    }

    [Fact]
    public void MissingMalformedOrUnknownConfigurationFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LedgerRuntimeOptions.FromConfiguration(Configuration()));
        Assert.Throws<InvalidOperationException>(() =>
            LedgerRuntimeOptions.FromConfiguration(Configuration(
                ("Ledger:Enabled", "sometimes"),
                ("Ledger:Provider", "InMemory"))));
        Assert.Throws<InvalidOperationException>(() =>
            LedgerRuntimeOptions.FromConfiguration(Configuration(
                ("Ledger:Enabled", "false"),
                ("Ledger:Provider", "CloudMagic"))));
    }

    [Fact]
    public void ExplicitDisabledConfigurationIsAcceptedButNotEnabled()
    {
        LedgerRuntimeOptions options = LedgerRuntimeOptions.FromConfiguration(Configuration(
            ("Ledger:Enabled", "false"),
            ("Ledger:Provider", "InMemory")));

        Assert.False(options.Enabled);
        Assert.False(options.UsePostgres);
    }

    [Fact]
    public void PostgresRequiresExistingServerConnectionConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LedgerRuntimeOptions.FromConfiguration(Configuration(
                ("Ledger:Enabled", "true"),
                ("Ledger:Provider", "Postgres"))));

        LedgerRuntimeOptions options = LedgerRuntimeOptions.FromConfiguration(Configuration(
            ("Ledger:Enabled", "true"),
            ("Ledger:Provider", "Postgres"),
            ("Ledger:ConnectionString", "Host=localhost;Database=redmist;Username=test"),
            ("Ledger:MigrationsDirectory", "Persistence/Migrations")));
        Assert.True(options.Enabled);
        Assert.True(options.UsePostgres);
        Assert.Contains("Host=localhost", options.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledInitializerIsAHardNoOpEvenForPostgresOptions()
    {
        var options = new LedgerRuntimeOptions(
            false,
            "Postgres",
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Timeout=1",
            "missing-migrations");
        var initializer = new LedgerDatabaseInitializationService(options);

        await initializer.StartAsync(CancellationToken.None);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => item.Value))
            .Build();
}
