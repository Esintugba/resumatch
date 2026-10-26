using Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace API.Tests.Integration.Infrastructure;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string DatabaseName = "resumatch_integration_tests";
    private const string Username = "resumatch_test";
    private const string Password = "testcontainers-only-password";

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17.6-alpine")
            .WithDatabase(DatabaseName)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();

    private bool _initialized;

    public string ConnectionString
    {
        get
        {
            EnsureInitialized();
            return _container.GetConnectionString();
        }
    }

    public TestDbContextFactory DbContextFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // StartAsync deliberately propagates Docker discovery/startup errors.
        // Integration tests must fail when their required infrastructure is
        // unavailable instead of being reported as skipped or successful.
        await _container.StartAsync();

        DbContextFactory = new TestDbContextFactory(
            _container.GetConnectionString());

        await using var dbContext = DbContextFactory.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException(
                "The integration test database has pending migrations after startup.");
        }

        _initialized = true;
    }

    public async Task ResetDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var tableNames = new List<string>();
        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                """
                SELECT tablename
                FROM pg_catalog.pg_tables
                WHERE schemaname = 'public'
                  AND tablename <> '__EFMigrationsHistory'
                ORDER BY tablename;
                """;

            await using var reader = await tableCommand.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        if (tableNames.Count == 0)
        {
            return;
        }

        var qualifiedTables = string.Join(
            ", ",
            tableNames.Select(tableName =>
                $"public.{QuoteIdentifier(tableName)}"));

        await using var truncateCommand = connection.CreateCommand();
        truncateCommand.CommandText =
            $"TRUNCATE TABLE {qualifiedTables} RESTART IDENTITY CASCADE;";
        await truncateCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "The PostgreSQL integration test fixture is not initialized.");
        }
    }
}
