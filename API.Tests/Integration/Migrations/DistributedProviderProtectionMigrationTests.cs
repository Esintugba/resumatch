using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class DistributedProviderProtectionMigrationTests(
    PostgreSqlFixture fixture) : IntegrationTestBase(fixture)
{
    private const string PreviousMigration =
        "20261003115357_HardenAiUsageBudgetLedgerConstraints";
    private const string CurrentMigration =
        "20261003123227_AddDistributedProviderProtection";
    private const string ProbeLeaseMigration =
        "20261003124621_AddDistributedCircuitProbeLeases";

    [Fact]
    public async Task CleanChain_CreatesProtectionTablesConstraintsAndIndexes()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiProviderConcurrencyLeases\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiProviderCircuitStates\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiProviderCircuitProbeLeases\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AiProviderCircuitStates_Lifecycle');"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AiProviderCircuitStates_ProviderScope');"));
    }

    [Fact]
    public async Task ProtectionMigration_ProbeLeaseUpgradeAndDown_AreReversible()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_probe_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using var context = factory.CreateDbContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
            await migrator.MigrateAsync(ProbeLeaseMigration, cancellation.Token);
            await using (var upgraded = new NpgsqlConnection(connectionString))
            {
                await upgraded.OpenAsync(cancellation.Token);
                Assert.True(await ExistsAsync(upgraded,
                    "SELECT to_regclass('public.\"AiProviderCircuitProbeLeases\"') IS NOT NULL;",
                    cancellation.Token));
            }

            await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
            await using var downgraded = new NpgsqlConnection(connectionString);
            await downgraded.OpenAsync(cancellation.Token);
            Assert.False(await ExistsAsync(downgraded,
                "SELECT to_regclass('public.\"AiProviderCircuitProbeLeases\"') IS NOT NULL;",
                cancellation.Token));
            Assert.True(await ExistsAsync(downgraded,
                "SELECT to_regclass('public.\"AiProviderCircuitStates\"') IS NOT NULL;",
                cancellation.Token));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task PreviousMigration_UpgradeAndDown_AreReversible()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_protection_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using var context = factory.CreateDbContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
            await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
            await using (var upgraded = new NpgsqlConnection(connectionString))
            {
                await upgraded.OpenAsync(cancellation.Token);
                Assert.True(await ExistsAsync(upgraded,
                    "SELECT to_regclass('public.\"AiProviderCircuitStates\"') IS NOT NULL;",
                    cancellation.Token));
            }

            await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
            await using var downgraded = new NpgsqlConnection(connectionString);
            await downgraded.OpenAsync(cancellation.Token);
            Assert.False(await ExistsAsync(downgraded,
                "SELECT to_regclass('public.\"AiProviderCircuitStates\"') IS NOT NULL;",
                cancellation.Token));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProtectionTables_HaveNoForeignKeysToDomainOrBudgetTables()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM pg_constraint
            WHERE contype = 'f'
              AND conrelid IN
              ('"AiProviderConcurrencyLeases"'::regclass,
               '"AiProviderCircuitStates"'::regclass,
               '"AiProviderCircuitProbeLeases"'::regclass);
            """;
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken token = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync(token))!;
    }

    private string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private async Task CreateDatabaseAsync(string databaseName, CancellationToken token)
    {
        await using var connection = await OpenAdministrationConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task DropDatabaseAsync(string databaseName, CancellationToken token)
    {
        await using var connection = await OpenAdministrationConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<NpgsqlConnection> OpenAdministrationConnectionAsync(
        CancellationToken token)
    {
        var builder = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(token);
        return connection;
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
