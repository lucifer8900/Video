using Lingmai.RedMist.Generation.Persistence;

namespace Lingmai.RedMist.Api.Ledger;

public sealed record LedgerRuntimeOptions(
    bool Enabled,
    string Provider,
    string? ConnectionString,
    string MigrationsDirectory)
{
    public const string SectionName = "Ledger";

    public bool UsePostgres => string.Equals(Provider, "Postgres", StringComparison.Ordinal);

    public static LedgerRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(SectionName);
        string enabledText = section["Enabled"]?.Trim() ?? string.Empty;
        if (!bool.TryParse(enabledText, out bool enabled))
            throw new InvalidOperationException("Ledger:Enabled must be explicitly configured as true or false.");
        string provider = section["Provider"]?.Trim() ?? string.Empty;
        string? connectionString = NullIfBlank(section["ConnectionString"]);
        string configuredMigrationsDirectory =
            section["MigrationsDirectory"]?.Trim() ?? string.Empty;
        string migrationsDirectory = string.IsNullOrWhiteSpace(configuredMigrationsDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Persistence", "Migrations")
            : Path.GetFullPath(configuredMigrationsDirectory, AppContext.BaseDirectory);

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return new LedgerRuntimeOptions(enabled, "InMemory", null, migrationsDirectory);
        }

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (connectionString is null)
            {
                throw new InvalidOperationException(
                    "Ledger:ConnectionString is required when Ledger:Provider is Postgres.");
            }

            return new LedgerRuntimeOptions(enabled, "Postgres", connectionString, migrationsDirectory);
        }

        throw new InvalidOperationException(
            "Ledger:Provider must be explicitly configured as InMemory or Postgres.");
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class LedgerDatabaseInitializationService : IHostedService
{
    private readonly LedgerRuntimeOptions _options;

    public LedgerDatabaseInitializationService(LedgerRuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.UsePostgres) return Task.CompletedTask;
        var migrator = new GenerationDatabaseMigrator(
            _options.ConnectionString!,
            _options.MigrationsDirectory);
        return migrator.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
