using Lingmai.RedMist.Generation;
using Lingmai.RedMist.Generation.Persistence;
using Npgsql;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresGenerationIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MigrationRejectsAnyInitialStatusOtherThanCreated()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO generation_jobs
                (id, idempotency_key, input_hash, status, created_at_utc, updated_at_utc)
            VALUES
                (@id, @key, @hash, 'queued', @now, @now)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("key", "postgres-illegal-initial-status");
        command.Parameters.AddWithValue("hash", $"sha256:{new string('0', 64)}");
        command.Parameters.AddWithValue("now", FixedNow);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        Assert.Contains(exception.SqlState, new[] { "23514", "P0001" });
    }

    [Fact]
    public async Task MigrationEnforcesTransitionsEvenWhenRepositoryIsBypassed()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var repository = new PostgresGenerationJobRepository(database.ConnectionString);
        var service = new GenerationJobService(repository, new FixedTimeProvider(FixedNow));
        GenerationJob created = await service.SubmitAsync(
            Submission("postgres-illegal-transition", '1'),
            CancellationToken.None);
        GenerationJob queued = await repository.TransitionAsync(
            created.Id,
            created.Version,
            GenerationJobStatus.Queued,
            CancellationToken.None);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE generation_jobs SET status = 'ready' WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", queued.Id);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        Assert.Contains(exception.SqlState, new[] { "23514", "P0001" });

        GenerationJob? persistedOrNull = await repository.GetAsync(created.Id, CancellationToken.None);
        Assert.NotNull(persistedOrNull);
        GenerationJob persisted = persistedOrNull!;
        Assert.Equal(GenerationJobStatus.Queued, persisted.Status);
    }

    [Fact]
    public async Task IndependentConnectionsConvergeOnOneIdempotentJob()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var firstService = new GenerationJobService(
            new PostgresGenerationJobRepository(database.ConnectionString),
            new FixedTimeProvider(FixedNow));
        var secondService = new GenerationJobService(
            new PostgresGenerationJobRepository(database.ConnectionString),
            new FixedTimeProvider(FixedNow));
        GenerationJobSubmission submission = Submission("postgres-idempotency-race", '2');

        Task<GenerationJob>[] requests = Enumerable.Range(0, 32)
            .Select(index => (index & 1) == 0
                ? firstService.SubmitAsync(submission, CancellationToken.None)
                : secondService.SubmitAsync(submission, CancellationToken.None))
            .ToArray();
        GenerationJob[] jobs = await Task.WhenAll(requests);

        Assert.Single(jobs.Select(job => job.Id).Distinct());
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM generation_jobs WHERE idempotency_key = 'postgres-idempotency-race'"));
    }

    [Fact]
    public async Task ExpiredLeaseCanBeClaimedAfterWorkerRestart()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var firstRepository = new PostgresGenerationJobRepository(database.ConnectionString);
        var secondRepository = new PostgresGenerationJobRepository(database.ConnectionString);
        var clock = new ManualTimeProvider(FixedNow);
        var service = new GenerationJobService(firstRepository, clock);
        GenerationJob job = await service.SubmitAsync(
            Submission("postgres-worker-restart", '3'),
            CancellationToken.None);
        job = await firstRepository.TransitionAsync(
            job.Id,
            job.Version,
            GenerationJobStatus.Queued,
            CancellationToken.None);
        job = await firstRepository.TransitionAsync(
            job.Id,
            job.Version,
            GenerationJobStatus.Generating,
            CancellationToken.None);
        GenerationJobLease? abandoned = await firstRepository.TryAcquireNextAsync(
            "worker-before-crash",
            clock.GetUtcNow(),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        var processor = new RecordingStageProcessor(GenerationJobStatus.Moderating);
        var restartedWorker = new GenerationJobWorker(
            secondRepository,
            processor,
            clock,
            new GenerationWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

        Assert.NotNull(abandoned);
        Assert.True(await restartedWorker.TryProcessOneAsync(
            "worker-after-restart",
            CancellationToken.None));
        GenerationJob? recoveredOrNull = await secondRepository.GetAsync(
            job.Id,
            CancellationToken.None);
        Assert.NotNull(recoveredOrNull);
        GenerationJob recovered = recoveredOrNull!;
        Assert.Equal(job.Id, recovered.Id);
        Assert.Equal(GenerationJobStatus.Moderating, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal(GenerationJobStatus.Generating, Assert.Single(processor.ObservedJobs).Status);
    }

    private static GenerationJobSubmission Submission(string key, char hashDigit) =>
        new(key, $"sha256:{new string(hashDigit, 64)}");

    private sealed class RecordingStageProcessor(GenerationJobStatus resultStatus)
        : IGenerationStageProcessor
    {
        public List<GenerationJob> ObservedJobs { get; } = [];

        public Task<GenerationJobStatus> ProcessAsync(
            GenerationJob job,
            CancellationToken cancellationToken)
        {
            ObservedJobs.Add(job);
            return Task.FromResult(resultStatus);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private const string ConnectionStringEnvironmentVariable =
            "REDMIST_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private PostgresTestDatabase(
            string adminConnectionString,
            string connectionString,
            string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            _schemaName = schemaName;
        }

        public string ConnectionString { get; }

        public static async Task<PostgresTestDatabase?> TryCreateAsync()
        {
            string? configured = Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured)) return null;

            string schemaName = "cx302_" + Guid.NewGuid().ToString("N");
            var isolatedBuilder = new NpgsqlConnectionStringBuilder(configured)
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
            return new PostgresTestDatabase(
                configured,
                isolatedBuilder.ConnectionString,
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
