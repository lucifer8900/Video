using Npgsql;

namespace Lingmai.RedMist.Generation.Persistence;

public sealed class GenerationDatabaseMigrator
{
    private const long MigrationLockKey = 7_321_906_448_302;
    private readonly string _connectionString;
    private readonly string _migrationsDirectory;

    public GenerationDatabaseMigrator(string connectionString, string migrationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        _connectionString = connectionString;
        _migrationsDirectory = Path.GetFullPath(migrationsDirectory);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_migrationsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Generation migration directory was not found: {_migrationsDirectory}");
        }

        string[] migrationPaths = Directory
            .EnumerateFiles(_migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteAsync(
                    connection,
                    transaction,
                    "SELECT pg_advisory_xact_lock(@lock_key)",
                    cancellationToken,
                    new NpgsqlParameter<long>("lock_key", MigrationLockKey))
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    CREATE TABLE IF NOT EXISTS generation_schema_migrations
                    (
                        migration_name text PRIMARY KEY,
                        applied_at_utc timestamptz NOT NULL
                    )
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (string migrationPath in migrationPaths)
            {
                string migrationName = Path.GetFileName(migrationPath);
                if (await IsAppliedAsync(
                        connection,
                        transaction,
                        migrationName,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    continue;
                }

                string migrationSql = await File
                    .ReadAllTextAsync(migrationPath, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(migrationSql))
                {
                    throw new InvalidDataException(
                        $"Generation migration is empty: {migrationName}");
                }

                await ExecuteAsync(
                        connection,
                        transaction,
                        migrationSql,
                        cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO generation_schema_migrations
                            (migration_name, applied_at_utc)
                        VALUES
                            (@migration_name, CURRENT_TIMESTAMP)
                        """,
                        cancellationToken,
                        new NpgsqlParameter<string>("migration_name", migrationName))
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM generation_schema_migrations
                WHERE migration_name = @migration_name
            )
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<string>("migration_name", migrationName));
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (parameters.Length > 0) command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
