using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class ParseDraftRetentionMigrationTests(PostgreSqlFixture fixture) :
    IntegrationTestBase(fixture)
{
    private const string PreviousMigration =
        "20261001110910_AddAnalysisSourceProvenance";
    private const string CurrentMigration =
        "20261001115228_AddParseDraftRetentionIndex";
    private const string IndexName = "IX_JobPostingParseDrafts_Retention";

    [Fact]
    public async Task Clean_chain_applies_retention_index_and_query_plan_can_use_it()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await AssertIndexExistsAsync(
            Fixture.ConnectionString,
            expected: true,
            cancellation.Token);

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellation.Token);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SET enable_seqscan = off;
            EXPLAIN (COSTS OFF)
            SELECT draft."Id"
            FROM "JobPostingParseDrafts" AS draft
            WHERE draft."Status" IN (3, 4, 5, 6, 7)
              AND CASE
                    WHEN draft."Status" = 4 THEN draft."ConfirmedAt"
                    WHEN draft."Status" = 3 THEN draft."CompletedAt"
                    WHEN draft."Status" = 5 THEN
                        COALESCE(draft."CompletedAt", draft."UpdatedAt")
                    WHEN draft."Status" IN (6, 7) THEN draft."UpdatedAt"
                  END <= now()
            ORDER BY
                CASE
                    WHEN draft."Status" = 4 THEN draft."ConfirmedAt"
                    WHEN draft."Status" = 3 THEN draft."CompletedAt"
                    WHEN draft."Status" = 5 THEN
                        COALESCE(draft."CompletedAt", draft."UpdatedAt")
                    WHEN draft."Status" IN (6, 7) THEN draft."UpdatedAt"
                END,
                draft."Id";
            """;
        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellation.Token);
        while (await reader.ReadAsync(cancellation.Token))
        {
            plan.Add(reader.GetString(0));
        }

        Assert.Contains(plan, line => line.Contains(IndexName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Previous_migration_up_and_down_are_reversible_without_data_columns()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseName = $"resumatch_retention_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using (var context = factory.CreateDbContext())
            {
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
                await AssertIndexExistsAsync(
                    connectionString, expected: false, cancellation.Token);
                await migrator.MigrateAsync(CurrentMigration, cancellation.Token);
                await AssertIndexExistsAsync(
                    connectionString, expected: true, cancellation.Token);
                await migrator.MigrateAsync(PreviousMigration, cancellation.Token);
                await AssertIndexExistsAsync(
                    connectionString, expected: false, cancellation.Token);
            }
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    private static async Task AssertIndexExistsAsync(
        string connectionString,
        bool expected,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname = @indexName
            );
            """;
        command.Parameters.AddWithValue("indexName", IndexName);
        Assert.Equal(expected, (bool)(await command.ExecuteScalarAsync(
            cancellationToken))!);
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

    private async Task CreateDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAdministrationConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAdministrationConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAdministrationConnectionAsync(
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
