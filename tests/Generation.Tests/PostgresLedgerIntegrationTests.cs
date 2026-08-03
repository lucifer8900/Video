using System.Text.Json;
using Lingmai.RedMist.Generation.Ledger;
using Lingmai.RedMist.Generation.Persistence;
using Npgsql;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresLedgerIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MigrationPersistsMonotonicIngestSequenceAndOpaqueTombstone()
    {
        await using PostgresLedgerTestDatabase? database =
            await PostgresLedgerTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var repository = new PostgresLedgerRepository(database.ConnectionString);
        var service = Service(repository);

        await service.AppendAsync([Event("led.persisted")], CancellationToken.None);
        NarrativeLedgerPage page = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 10, null, CancellationToken.None);
        Assert.True(Assert.Single(page.Entries).IngestSequence > 0);

        await repository.DeletePlayerAsync("p.8842", CancellationToken.None);
        string playerHash = await database.ExecuteScalarAsync<string>(
            "SELECT player_hash FROM ledger_player_tombstones");
        Assert.StartsWith("sha256:", playerHash, StringComparison.Ordinal);
        Assert.DoesNotContain("p.8842", playerHash, StringComparison.Ordinal);
        Assert.Equal(0L, await database.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM narrative_ledger_events"));
    }

    [Fact]
    public async Task IndependentRepositoryConflictIsAtomic()
    {
        await using PostgresLedgerTestDatabase? database =
            await PostgresLedgerTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var first = Service(new PostgresLedgerRepository(database.ConnectionString));
        var restarted = Service(new PostgresLedgerRepository(database.ConnectionString));
        await first.AppendAsync(
            [Event("led.existing", payload: "{\"debtKind\":\"spared_life\"}")],
            CancellationToken.None);

        await Assert.ThrowsAsync<LedgerIdempotencyConflictException>(() => restarted.AppendAsync(
            [
                Event("led.must-rollback"),
                Event("led.existing", payload: "{\"debtKind\":\"material_debt\"}"),
            ],
            CancellationToken.None));

        Assert.Equal(1, await new PostgresLedgerRepository(database.ConnectionString)
            .CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LateAppendAndTombstoneSurviveRepositoryRestart()
    {
        await using PostgresLedgerTestDatabase? database =
            await PostgresLedgerTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var firstRepository = new PostgresLedgerRepository(database.ConnectionString);
        var first = Service(firstRepository);
        await first.AppendAsync(
            [Event("led.first", worldClock: 100), Event("led.second", worldClock: 200)],
            CancellationToken.None);
        NarrativeLedgerPage initial = await first.QueryAsync(
            "p.8842", "chapter.red_mist", 1, null, CancellationToken.None);

        var restartedRepository = new PostgresLedgerRepository(database.ConnectionString);
        var restarted = Service(restartedRepository);
        await restarted.AppendAsync([Event("led.late", worldClock: 1)], CancellationToken.None);
        NarrativeLedgerPage remainder = await restarted.QueryAsync(
            "p.8842", "chapter.red_mist", 10, initial.NextCursor, CancellationToken.None);
        Assert.Equal(
            ["led.second", "led.late"],
            remainder.Entries.Select(entry => entry.Event.EntryId));

        await restartedRepository.DeletePlayerAsync("p.8842", CancellationToken.None);
        var afterRestart = Service(new PostgresLedgerRepository(database.ConnectionString));
        await Assert.ThrowsAsync<LedgerPlayerDeletedException>(() => afterRestart.AppendAsync(
            [Event("led.offline-retry")],
            CancellationToken.None));
    }

    private static LedgerService Service(ILedgerRepository repository) =>
        new(repository, new FixedTimeProvider(FixedNow));

    private static LedgerEventData Event(
        string entryId,
        long worldClock = 1440,
        string payload = "{\"debtKind\":\"spared_life\"}") =>
        new(
            "1.0.0",
            entryId,
            "p.8842",
            "debt_incurred",
            ["char.shen_yan", "npc.shi_jun"],
            3,
            "chapter.red_mist",
            worldClock,
            "rescue",
            ["fact.shijun_alive"],
            Parse(payload));

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class PostgresLedgerTestDatabase : IAsyncDisposable
    {
        private const string ConnectionStringEnvironmentVariable =
            "REDMIST_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private PostgresLedgerTestDatabase(
            string adminConnectionString,
            string connectionString,
            string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            _schemaName = schemaName;
        }

        public string ConnectionString { get; }

        public static async Task<PostgresLedgerTestDatabase?> TryCreateAsync()
        {
            string? configured = Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured)) return null;

            string schemaName = "cx401_" + Guid.NewGuid().ToString("N");
            var isolated = new NpgsqlConnectionStringBuilder(configured)
            {
                SearchPath = schemaName,
                IncludeErrorDetail = false,
            };
            await using var connection = new NpgsqlConnection(configured);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE SCHEMA \"{schemaName}\"",
                connection);
            await command.ExecuteNonQueryAsync();
            return new PostgresLedgerTestDatabase(
                configured,
                isolated.ConnectionString,
                schemaName);
        }

        public async Task MigrateAsync()
        {
            var migrator = new GenerationDatabaseMigrator(
                ConnectionString,
                Path.Combine(
                    FindRepositoryRoot(),
                    "server",
                    "Generation",
                    "Persistence",
                    "Migrations"));
            await migrator.MigrateAsync(CancellationToken.None);
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            object? value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Video.sln")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        }
    }
}
