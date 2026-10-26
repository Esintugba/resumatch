using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class AiUsageBudgetLedgerMigrationTests(PostgreSqlFixture fixture) :
    IntegrationTestBase(fixture)
{
    private const string PreviousMigration =
        "20261001115228_AddParseDraftRetentionIndex";
    private const string CurrentMigration =
        "20261003115357_HardenAiUsageBudgetLedgerConstraints";

    [Fact]
    public async Task CleanChain_CreatesLedgerTablesConstraintsAndUniqueIndexes()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiUsageBudgetPeriods\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiUsageReservations\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT to_regclass('public.\"AiUsageReservationPeriods\"') IS NOT NULL;"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UX_AiUsageBudgetPeriods_System');"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AiUsageReservations_Status');"));
    }

    [Fact]
    public async Task PreviousMigration_UpgradeAndDown_AreReversible()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_budget_{Guid.NewGuid():N}";
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
                    "SELECT to_regclass('public.\"AiUsageReservations\"') IS NOT NULL;",
                    cancellation.Token));
            }

            await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
            await using var downgraded = new NpgsqlConnection(connectionString);
            await downgraded.OpenAsync(cancellation.Token);
            Assert.False(await ExistsAsync(downgraded,
                "SELECT to_regclass('public.\"AiUsageReservations\"') IS NOT NULL;",
                cancellation.Token));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task LedgerHasNoForeignKeyToDraftOrUser()
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
              (
                  '"AiUsageReservations"'::regclass,
                  '"AiUsageBudgetPeriods"'::regclass
              );
            """;
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
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
