using Lingmai.RedMist.Generation.Budget;
using Lingmai.RedMist.Generation.Persistence;
using Lingmai.RedMist.Generation.Tests.TestDoubles;
using Npgsql;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresBudgetIntegrationTests
{
    [Fact]
    public async Task IndependentConnectionsCannotOversubscribeSharedFiveDimensionBudget()
    {
        await using PostgresBudgetTestDatabase database =
            await PostgresBudgetTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var time = new FixedBudgetTimeProvider(SettlementBudgetTestData.FixedNow);
        var first = new PostgresBudgetRepository(database.ConnectionString, time);
        var second = new PostgresBudgetRepository(database.ConnectionString, time);
        await first.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(100),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);

        Task<BudgetReserveDecision>[] attempts = Enumerable.Range(0, 16)
            .Select(index => (index & 1) == 0 ? first : second)
            .Select((repository, index) => repository.ReserveAsync(
                SettlementBudgetTestData.Request($"postgres-budget-race-{index:D2}", 50, 60),
                CancellationToken.None))
            .ToArray();
        BudgetReserveDecision[] results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result.Admitted));
        Assert.Equal(15, results.Count(result => !result.Admitted));
        Assert.Equal(
            5,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM budget_reservation_scopes"));
    }

    [Fact]
    public async Task OverageSettlementIsDurableAndTripsEveryScopeCircuit()
    {
        await using PostgresBudgetTestDatabase database =
            await PostgresBudgetTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var repository = new PostgresBudgetRepository(
            database.ConnectionString,
            new FixedBudgetTimeProvider(SettlementBudgetTestData.FixedNow));
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        BudgetReserveDecision decision = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("postgres-overage-settlement", 300, 400),
            CancellationToken.None);
        BudgetReservation reservation = Assert.IsType<BudgetReservation>(decision.Reservation);

        BudgetReservation settled = await repository.SettleAsync(
            reservation.Id,
            "postgres-provider-invoice",
            550,
            CancellationToken.None);

        Assert.True(settled.EstimatorExceeded);
        Assert.True(settled.CircuitOpened);
        Assert.Equal(
            5,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM budget_limits WHERE circuit_open = TRUE AND consumed_micros = 550"));
        BudgetReserveDecision blocked = await repository.ReserveAsync(
            SettlementBudgetTestData.Request("postgres-after-overage", 1, 1),
            CancellationToken.None);
        Assert.False(blocked.Admitted);
    }

    [Fact]
    public async Task ConcurrentSameKeyReservationsReplayOneDatabaseReservation()
    {
        await using PostgresBudgetTestDatabase database =
            await PostgresBudgetTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var time = new FixedBudgetTimeProvider(SettlementBudgetTestData.FixedNow);
        var first = new PostgresBudgetRepository(database.ConnectionString, time);
        var second = new PostgresBudgetRepository(database.ConnectionString, time);
        await first.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        BudgetReservationRequest request =
            SettlementBudgetTestData.Request("postgres-same-key-race", 100, 200);

        Task<BudgetReserveDecision>[] attempts = Enumerable.Range(0, 32)
            .Select(index => (index & 1) == 0 ? first : second)
            .Select(repository => repository.ReserveAsync(request, CancellationToken.None))
            .ToArray();
        BudgetReserveDecision[] results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.True(result.Admitted));
        Assert.Single(results.Select(result => result.Reservation!.Id).Distinct());
        Assert.Equal(
            5,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM budget_reservation_scopes"));
        Assert.Equal(
            1000,
            await database.ExecuteScalarAsync<long>(
                "SELECT sum(reserved_micros) FROM budget_limits"));
    }

    [Fact]
    public async Task ConcurrentSameSettlementEventReplaysOneLedgerEvent()
    {
        await using PostgresBudgetTestDatabase database =
            await PostgresBudgetTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var time = new FixedBudgetTimeProvider(SettlementBudgetTestData.FixedNow);
        var first = new PostgresBudgetRepository(database.ConnectionString, time);
        var second = new PostgresBudgetRepository(database.ConnectionString, time);
        await first.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        BudgetReserveDecision decision = await first.ReserveAsync(
            SettlementBudgetTestData.Request("postgres-event-race", 100, 200),
            CancellationToken.None);
        Guid reservationId = Assert.IsType<BudgetReservation>(decision.Reservation).Id;

        BudgetReservation[] results = await Task.WhenAll(
            first.SettleAsync(reservationId, "postgres-shared-event", 125, CancellationToken.None),
            second.SettleAsync(reservationId, "postgres-shared-event", 125, CancellationToken.None));

        Assert.Equal(results[0], results[1]);
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM budget_events WHERE event_id = 'postgres-shared-event'"));
    }

    [Fact]
    public async Task ShanghaiDailyLimitUsesTheDatabasePeriodAcrossUtcDateBoundary()
    {
        var boundary = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        await using PostgresBudgetTestDatabase database =
            await PostgresBudgetTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var repository = new PostgresBudgetRepository(
            database.ConnectionString,
            new FixedBudgetTimeProvider(boundary));
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            boundary,
            CancellationToken.None);
        BudgetReservationRequest request = SettlementBudgetTestData.Request(
            "postgres-shanghai-boundary",
            100,
            200) with
        {
            RequestedAtUtc = boundary.AddDays(-1),
        };

        BudgetReserveDecision decision = await repository.ReserveAsync(
            request,
            CancellationToken.None);

        Assert.True(decision.Admitted);
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM budget_reservations WHERE daily_scope_key LIKE '%:2026-07-17'"));
    }

    private sealed class PostgresBudgetTestDatabase : IAsyncDisposable
    {
        private const string ConnectionStringEnvironmentVariable =
            "REDMIST_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private PostgresBudgetTestDatabase(
            string adminConnectionString,
            string connectionString,
            string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            _schemaName = schemaName;
        }

        public string ConnectionString { get; }

        public static async Task<PostgresBudgetTestDatabase> CreateAsync()
        {
            string? configured = Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException(
                    $"{ConnectionStringEnvironmentVariable} is required for explicit integration-test runs.");

            string schemaName = "cx305_" + Guid.NewGuid().ToString("N");
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
            return new PostgresBudgetTestDatabase(
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
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Video.sln")))
                    return current.FullName;
                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        }
    }
}
