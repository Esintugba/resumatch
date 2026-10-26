using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class AiUsageReconciliationLifecycleMigrationTests(
    PostgreSqlFixture fixture) : IntegrationTestBase(fixture)
{
    private const string PreviousMigration =
        "20261003124621_AddDistributedCircuitProbeLeases";
    private const string CurrentMigration =
        "20261003130702_HardenAiUsageReconciliationLifecycle";

    [Fact]
    public async Task CleanChain_CreatesLifecycleColumnsConstraintsAndIndexes()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AiUsageReservations' AND column_name = 'ReconciliationReason');"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AiUsageReservations_Overage');"));
        Assert.True(await ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AiUsageReservations_ProviderRequestId');"));
    }

    [Fact]
    public async Task PreviousMigration_UpgradeAndDown_AreReversibleWithoutOverageData()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_reconciliation_{Guid.NewGuid():N}";
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
                    "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AiUsageReservations' AND column_name = 'OverageTotalTokens');",
                    cancellation.Token));
            }

            await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
            await using var downgraded = new NpgsqlConnection(connectionString);
            await downgraded.OpenAsync(cancellation.Token);
            Assert.False(await ExistsAsync(downgraded,
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AiUsageReservations' AND column_name = 'OverageTotalTokens');",
                cancellation.Token));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Upgrade_BackfillsLegacyUnresolvedUsageWithoutInventingResolution()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_reconciliation_backfill_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using var context = factory.CreateDbContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
            var reservationId = Guid.NewGuid();
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellation.Token);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO "AiUsageReservations"
                    ("Id", "UserId", "DraftId", "AttemptCount", "AttemptKey",
                     "ExpiresAt", "ReservedRequestCount", "ReservedInputTokens",
                     "ReservedOutputTokens", "ReservedTotalTokens", "Status",
                     "ReconciliationRequiredAt", "ActualInputTokens",
                     "ActualOutputTokens", "ActualTotalTokens", "CreatedAt")
                    VALUES
                    (@id, @userId, @draftId, 1, @attemptKey, @expiresAt, 1,
                     100, 50, 150, 4, @requiredAt, 120, 80, 200, @createdAt);
                    """;
                command.Parameters.AddWithValue("id", reservationId);
                command.Parameters.AddWithValue("userId", Guid.NewGuid());
                command.Parameters.AddWithValue("draftId", Guid.NewGuid());
                command.Parameters.AddWithValue("attemptKey", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("expiresAt", DateTimeOffset.UtcNow.AddHours(1));
                command.Parameters.AddWithValue("requiredAt", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow.AddMinutes(-1));
                await command.ExecuteNonQueryAsync(cancellation.Token);
            }

            await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
            await using var verification = new NpgsqlConnection(connectionString);
            await verification.OpenAsync(cancellation.Token);
            await using var verify = verification.CreateCommand();
            verify.CommandText =
                """
                SELECT "Status", "ReconciliationReason", "OverageTotalTokens"
                FROM "AiUsageReservations"
                WHERE "Id" = @id;
                """;
            verify.Parameters.AddWithValue("id", reservationId);
            await using var reader = await verify.ExecuteReaderAsync(cancellation.Token);
            Assert.True(await reader.ReadAsync(cancellation.Token));
            Assert.Equal(4, reader.GetInt32(0));
            Assert.Equal(3, reader.GetInt32(1));
            Assert.Equal(50L, reader.GetInt64(2));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task DownMigration_RefusesToDiscardAccountingOverage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_reconciliation_down_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using var context = factory.CreateDbContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellation.Token);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO "AiUsageBudgetPeriods"
                    ("Id", "ScopeType", "UserId", "PeriodType", "PeriodStart",
                     "PeriodEnd", "RequestLimit", "TokenLimit", "ReservedRequests",
                     "ReservedTokens", "ConsumedRequests", "ConsumedInputTokens",
                     "ConsumedOutputTokens", "ConsumedTotalTokens", "CreatedAt")
                    VALUES
                    (@id, 0, NULL, 0, @start, @end, 1, 100, 0, 0, 1,
                     101, 0, 101, @createdAt);
                    """;
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                var periodStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
                command.Parameters.AddWithValue("start", periodStart);
                command.Parameters.AddWithValue("end", periodStart.AddDays(1));
                command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
                await command.ExecuteNonQueryAsync(cancellation.Token);
            }

            await Assert.ThrowsAsync<PostgresException>(() =>
                migrator.MigrateAsync(PreviousMigration, cancellation.Token));
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
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
