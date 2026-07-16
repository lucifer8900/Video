using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;
using Lingmai.RedMist.Generation.Persistence;
using Npgsql;
using System.Text.Json;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresAssociationAuditIntegrationTests
{
    [Fact]
    public async Task AuditRoundTripsAcrossRepositoryRestartAndReusesReadyGenerationJob()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        AssociationDecisionAuditContract audit = Audit(
            "genjob.audit.postgres.1",
            "thr.audit.postgres.1",
            "p.postgres_one");

        var writer = new PostgresAssociationDecisionAuditRepository(database.ConnectionString);
        await writer.StoreAsync(audit, CancellationToken.None);
        var reader = new PostgresAssociationDecisionAuditRepository(database.ConnectionString);
        AssociationDecisionAuditContract? persisted = await reader.GetAsync(
            audit.AuditRef,
            CancellationToken.None);

        Assert.Equal(
            JsonSerializer.Serialize(audit),
            JsonSerializer.Serialize(persisted));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM generation_jobs WHERE workload_kind = 'association_decision' AND status = 'ready'"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM generation_job_association_audits"));
        Assert.Equal(
            0,
            await new PostgresGenerationJobRepository(database.ConnectionString)
                .CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExactReplayIsIdempotentConflictFailsAndPlayerDeleteRemovesBaseRow()
    {
        await using PostgresTestDatabase? database = await PostgresTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        AssociationDecisionAuditContract audit = Audit(
            "genjob.audit.postgres.2",
            "thr.audit.postgres.2",
            "p.postgres_two");
        var repository = new PostgresAssociationDecisionAuditRepository(database.ConnectionString);

        await repository.StoreAsync(audit, CancellationToken.None);
        await repository.StoreAsync(audit, CancellationToken.None);
        await Assert.ThrowsAsync<AssociationDecisionAuditConflictException>(async () =>
            await repository.StoreAsync(
                audit with { TemplateId = "assoc.conflict.v1" },
                CancellationToken.None));
        Assert.Equal(1, await repository.DeletePlayerAsync(
            "p.postgres_two",
            CancellationToken.None));

        Assert.Null(await repository.GetAsync(audit.AuditRef, CancellationToken.None));
        Assert.Equal(0, await database.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM generation_jobs WHERE workload_kind = 'association_decision'"));
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
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = "npc.known",
        },
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
                "snapshot.audit.postgres.1"),
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

            string schemaName = "cx406_" + Guid.NewGuid().ToString("N");
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
