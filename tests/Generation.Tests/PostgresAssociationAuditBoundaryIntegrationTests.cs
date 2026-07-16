using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Persistence;
using Npgsql;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresAssociationAuditBoundaryIntegrationTests
{
    [Fact]
    public async Task DeletePlayerPermanentlySuppressesLaterPostgresAuditStores()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        const string playerId = "p.postgres_deleted";
        AssociationDecisionAuditContract original = Audit(
            "genjob.audit.postgres.deleted.1",
            "thr.audit.postgres.deleted.1",
            playerId);
        AssociationDecisionAuditContract attemptedResurrection = Audit(
            "genjob.audit.postgres.deleted.2",
            "thr.audit.postgres.deleted.2",
            playerId);
        var repository = new PostgresAssociationDecisionAuditRepository(
            database.ConnectionString);

        await repository.StoreAsync(original, CancellationToken.None);
        Assert.Equal(1, await repository.DeletePlayerAsync(playerId, CancellationToken.None));

        Exception? exception = await Record.ExceptionAsync(async () =>
            await repository.StoreAsync(attemptedResurrection, CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Equal("AssociationDecisionAuditSuppressedException", exception!.GetType().Name);
        Assert.Null(await repository.GetAsync(
            attemptedResurrection.AuditRef,
            CancellationToken.None));
    }

    [Fact]
    public async Task MediaSubmissionUsingExistingAssociationAuditRefIsIdempotencyConflict()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        AssociationDecisionAuditContract audit = Audit(
            "genjob.audit.postgres.media_collision.1",
            "thr.audit.postgres.media_collision.1",
            "p.postgres_media_collision");
        await new PostgresAssociationDecisionAuditRepository(database.ConnectionString)
            .StoreAsync(audit, CancellationToken.None);
        var service = new GenerationJobService(
            new PostgresGenerationJobRepository(database.ConnectionString),
            TimeProvider.System);

        IdempotencyConflictException exception =
            await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SubmitAsync(
                new GenerationJobSubmission(audit.AuditRef, audit.InputHash),
                CancellationToken.None));

        Assert.Equal(audit.AuditRef, exception.IdempotencyKey);
    }

    private static AssociationDecisionAuditContract Audit(
        string auditRef,
        string threadId,
        string playerId) => new(
        "1.0.0",
        auditRef,
        threadId,
        $"sha256:{new string('a', 64)}",
        AssociationAuditHash.ComputePlayerRefHash(playerId),
        "chapter.red_mist",
        "assoc.enemy.echo.v1",
        new Dictionary<string, string>(StringComparer.Ordinal),
        new[]
        {
            new AssociationProviderAuditContract(
                "ranker",
                "mock.association-ranker",
                "prompt.association-ranker.mock.v1",
                "model.association-ranker.mock.v1",
                true),
        },
        new[]
        {
            new AssociationGuardResultAuditContract(
                "deterministic",
                "passed",
                "none",
                "snapshot.audit.postgres.boundary.1"),
        },
        1,
        false,
        null,
        5,
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

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

            string schemaName = "cx406_boundary_" + Guid.NewGuid().ToString("N");
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
