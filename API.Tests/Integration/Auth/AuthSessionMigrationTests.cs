using Api.Data;
using Api.Entities;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Auth;

/// <summary>Runs the session migration on a separate database in the test container.</summary>
public sealed class AuthSessionMigrationTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string PreviousMigration = "20261103160850_AddSkillSearchAliases";
    private const string SessionMigration = "20261103173237_AddAuthSessions";

    private readonly string _databaseName = $"sessions_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(Fixture.ConnectionString, $"CREATE DATABASE \"{_databaseName}\";");
        _connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;
    }

    public override async Task DisposeAsync()
    {
        await ExecuteAsync(
            Fixture.ConnectionString,
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);");
        await base.DisposeAsync();
    }

    [Fact]
    public async Task UpDownUp_KeepsUsersAndGivesExistingUsersAStamp()
    {
        await MigrateAsync(PreviousMigration);
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            _connectionString,
            $"""
            INSERT INTO "Users" ("Id", "FirstName", "LastName", "Email", "PasswordHash", "IsActive", "IsEmailVerified", "CreatedAt")
            VALUES ('{userId}', 'Existing', 'User', 'existing@example.com', 'hash', true, false, now());
            """);

        await MigrateAsync(SessionMigration);
        Assert.Equal(1L, await ScalarAsync(
            """SELECT count(*) FROM "Users" WHERE "SecurityStamp" IS NOT NULL;"""));
        await using (var dbContext = CreateContext())
        {
            var now = DateTimeOffset.UtcNow;
            var session = new AuthSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now,
                LastUsedAt = now,
                IdleExpiresAt = now.AddDays(1),
                AbsoluteExpiresAt = now.AddDays(2)
            };
            dbContext.AuthSessions.Add(session);
            dbContext.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                TokenHash = new byte[32],
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync();
        }

        await MigrateAsync(PreviousMigration);

        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM "Users";"""));
        Assert.Equal(0L, await ScalarAsync(
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_name IN ('AuthSessions', 'RefreshTokens');
            """));
        Assert.Equal(0L, await ScalarAsync(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'Users' AND column_name = 'SecurityStamp';
            """));

        await MigrateAsync(SessionMigration);

        await using var verification = CreateContext();
        Assert.Equal(userId, (await verification.Users.SingleAsync()).Id);
        Assert.Equal(0, await verification.AuthSessions.CountAsync());
    }

    [Theory]
    [InlineData("""UPDATE "AuthSessions" SET "IdleExpiresAt" = "AbsoluteExpiresAt" + interval '1 day';""")]
    [InlineData("""UPDATE "AuthSessions" SET "RevokedAt" = now();""")]
    [InlineData("""INSERT INTO "RefreshTokens" ("Id", "SessionId", "TokenHash", "CreatedAt") SELECT gen_random_uuid(), "Id", '\x00'::bytea, now() FROM "AuthSessions";""")]
    public async Task Constraints_RejectInconsistentRows(string sql)
    {
        await MigrateAsync(SessionMigration);
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            _connectionString,
            $"""
            INSERT INTO "Users" ("Id", "FirstName", "LastName", "Email", "PasswordHash", "IsActive", "IsEmailVerified", "CreatedAt")
            VALUES ('{userId}', 'Check', 'User', 'check@example.com', 'hash', true, false, now());
            INSERT INTO "AuthSessions" ("Id", "UserId", "LastUsedAt", "IdleExpiresAt", "AbsoluteExpiresAt", "CreatedAt")
            VALUES (gen_random_uuid(), '{userId}', now(), now() + interval '1 day', now() + interval '2 days', now());
            """);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(_connectionString, sql));
    }

    private AppDbContext CreateContext() =>
        new TestDbContextFactory(_connectionString).CreateDbContext();

    private async Task MigrateAsync(string target)
    {
        await using var dbContext = CreateContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
