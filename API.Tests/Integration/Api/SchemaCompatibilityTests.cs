using System.Net;
using Api.Data;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Api;

public sealed class SchemaCompatibilityTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExactMigrationChain_IsReadyAndReportsSeparateSchemaCheck()
    {
        using var cancellation = CreateCancellation();
        using var application = new ResuMatchWebApplicationFactory(Fixture);
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync(
            "/health/ready",
            cancellation.Token);
        var payload = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"database\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"schema\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseOneMigrationBehind_IsUnreadyAndStartupDoesNotMigrate()
    {
        using var cancellation = CreateCancellation();
        var (previous, latest) = await GetLastTwoMigrationsAsync(
            cancellation.Token);

        await MigrateAsync(previous, cancellation.Token);
        try
        {
            var before = await ReadMigrationHistoryAsync(cancellation.Token);
            using var application = new ResuMatchWebApplicationFactory(Fixture);
            using var client = application.CreateHttpsClient();

            using var response = await client.GetAsync(
                "/health/ready",
                cancellation.Token);
            var payload = await response.Content.ReadAsStringAsync(
                cancellation.Token);
            var after = await ReadMigrationHistoryAsync(cancellation.Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("\"name\":\"schema\"", payload, StringComparison.Ordinal);
            Assert.Equal(before, after);
            Assert.DoesNotContain(latest, after);
        }
        finally
        {
            await MigrateAsync(latest, CancellationToken.None);
        }
    }

    [Fact]
    public async Task DatabaseWithUnknownMigration_IsUnready()
    {
        using var cancellation = CreateCancellation();
        const string unknown = "99999999999999_UnknownFutureMigration";
        await ExecuteAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@id, '10.0.12');",
            cancellation.Token,
            new NpgsqlParameter("id", unknown));
        try
        {
            using var application = new ResuMatchWebApplicationFactory(Fixture);
            using var client = application.CreateHttpsClient();

            using var response = await client.GetAsync(
                "/health/ready",
                cancellation.Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            await ExecuteAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id;",
                CancellationToken.None,
                new NpgsqlParameter("id", unknown));
        }
    }

    [Fact]
    public async Task MigrationHistoryUnavailable_IsUnreadyAndNotRecreated()
    {
        using var cancellation = CreateCancellation();
        await ExecuteAsync(
            "ALTER TABLE \"__EFMigrationsHistory\" RENAME TO \"__EFMigrationsHistory_unavailable\";",
            cancellation.Token);
        try
        {
            using var application = new ResuMatchWebApplicationFactory(Fixture);
            using var client = application.CreateHttpsClient();

            using var response = await client.GetAsync(
                "/health/ready",
                cancellation.Token);
            var historyExists = await ScalarAsync<bool>(
                "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL;",
                cancellation.Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False(historyExists);
        }
        finally
        {
            await ExecuteAsync(
                "ALTER TABLE \"__EFMigrationsHistory_unavailable\" RENAME TO \"__EFMigrationsHistory\";",
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task SchemaCheck_IsReadOnlyForHistoryAndApplicationTables()
    {
        using var cancellation = CreateCancellation();
        var historyBefore = await ReadMigrationHistoryAsync(cancellation.Token);
        var tablesBefore = await ReadPublicTablesAsync(cancellation.Token);
        using var application = new ResuMatchWebApplicationFactory(Fixture);
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync(
            "/health/ready",
            cancellation.Token);

        var historyAfter = await ReadMigrationHistoryAsync(cancellation.Token);
        var tablesAfter = await ReadPublicTablesAsync(cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(tablesBefore, tablesAfter);
    }

    private async Task<(string Previous, string Latest)> GetLastTwoMigrationsAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        cancellationToken.ThrowIfCancellationRequested();
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.True(migrations.Length >= 2);
        return (migrations[^2], migrations[^1]);
    }

    private async Task MigrateAsync(
        string targetMigration,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync(
            targetMigration,
            cancellationToken);
    }

    private Task<string[]> ReadMigrationHistoryAsync(
        CancellationToken cancellationToken) =>
        ReadStringsAsync(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";",
            cancellationToken);

    private Task<string[]> ReadPublicTablesAsync(
        CancellationToken cancellationToken) =>
        ReadStringsAsync(
            "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public' ORDER BY tablename;",
            cancellationToken);

    private async Task<string[]> ReadStringsAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(90));
}
