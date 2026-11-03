using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using API.Tests.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Api;

/// <summary>Runs the catalog migration on a separate database in the test container.</summary>
public sealed class SkillCatalogMigrationTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string PreviousMigration =
        "20261003130702_HardenAiUsageReconciliationLifecycle";
    private const string CatalogMigration = "20261101190634_AddSkillCatalogV1";
    private const string LatestMigration = "20261103160850_AddSkillSearchAliases";

    private readonly string _databaseName = $"catalog_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\";";
        await command.ExecuteNonQueryAsync();
        _connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;
    }

    public override async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task V1_InsertedExactlyTheV1CatalogFile()
    {
        await MigrateAsync(CatalogMigration);

        var skills = await ReadSkillsAsync();
        var expected = SkillCatalogFile.Read(version: 1);

        Assert.Equal(expected.Count, skills.Count);
        Assert.All(expected, row =>
        {
            var skill = Assert.Single(skills, item => item.Name == row.Name);
            Assert.Equal(row.Category, skill.Category);
            Assert.Equal(row.NormalizedName, skill.NormalizedName);
        });
        var hash = Hash(skills);
        Assert.True(hash == V1CatalogHash, $"Catalog hash is {hash}.");
    }

    [Fact]
    public async Task Latest_HoldsExactlyTheCurrentCatalogFile()
    {
        await MigrateAsync(LatestMigration);

        await using var dbContext = CreateContext();
        var skills = await dbContext.Skills.AsNoTracking().ToListAsync();
        var expected = SkillCatalogFile.Read();

        Assert.Equal(expected.Count, skills.Count);
        Assert.All(expected, row =>
        {
            var skill = Assert.Single(skills, item => item.Name == row.Name);
            Assert.Equal(row.Category, skill.Category);
            Assert.Equal(row.NormalizedName, skill.NormalizedName);
            Assert.Equal(row.NameTr, skill.NameTr);
            Assert.Equal(row.NormalizedNameTr, skill.NormalizedNameTr);
            Assert.Equal(row.SearchAliases, skill.SearchAliases);
        });
        var hash = Hash(await ReadSkillsAsync());
        Assert.True(hash == CurrentCatalogHash, $"Catalog hash is {hash}.");
    }

    [Fact]
    public async Task CatalogHash_DoesNotDependOnAliases()
    {
        await MigrateAsync(LatestMigration);
        var original = Hash(await ReadSkillsAsync());

        await ExecuteAsync("""UPDATE "Skills" SET "SearchAliases" = '{}';""");
        var withoutAliases = Hash(await ReadSkillsAsync());
        await ExecuteAsync("""UPDATE "Skills" SET "SearchAliases" = ARRAY['x', 'y'] WHERE "NormalizedName" = 'go';""");
        var changedAliases = Hash(await ReadSkillsAsync());

        Assert.Equal(CurrentCatalogHash, original);
        Assert.Equal(original, withoutAliases);
        Assert.Equal(original, changedAliases);
    }

    [Fact]
    public async Task Up_KeepsARowThatAlreadyUsesAName()
    {
        await MigrateAsync(PreviousMigration);
        var existingId = Guid.NewGuid();
        await ExecuteAsync(
            $"""
            INSERT INTO "Skills" ("Id", "Name", "NormalizedName", "Category", "CreatedAt")
            VALUES ('{existingId}', 'Teamwork', 'teamwork', {(int)SkillCategory.Other}, now());
            """);

        await MigrateAsync(LatestMigration);

        await using var dbContext = CreateContext();
        var skill = await dbContext.Skills.AsNoTracking()
            .SingleAsync(item => item.NormalizedName == "teamwork");
        Assert.Equal(existingId, skill.Id);
        Assert.Equal(SkillCategory.Other, skill.Category);
        Assert.Equal("Takım çalışması", skill.NameTr);
        Assert.Equal(400, await dbContext.Skills.CountAsync());
    }

    [Fact]
    public async Task UpDownUp_RemovesOnlyUnusedRowsAndRestoresTheCatalog()
    {
        await MigrateAsync(LatestMigration);
        var skillId = await SeedResumeUsingSkillAsync("react");

        await MigrateAsync(PreviousMigration);

        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM "Skills";"""));
        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM "ResumeSkills";"""));
        Assert.Equal(0L, await ScalarAsync(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE column_name IN ('NameTr', 'NormalizedNameTr', 'UnlistedSkillsNote', 'SearchAliases');
            """));

        await MigrateAsync(LatestMigration);

        await using var dbContext = CreateContext();
        Assert.Equal(400, await dbContext.Skills.CountAsync());
        Assert.Equal(
            skillId,
            (await dbContext.ResumeSkills.AsNoTracking().SingleAsync()).SkillId);
    }

    [Theory]
    [InlineData(false, 399L)]
    [InlineData(true, 400L)]
    public async Task AliasMigration_UpDownUp_KeepsAReferencedNewSkill(bool referenced, long countAfterDown)
    {
        await MigrateAsync(LatestMigration);
        if (referenced)
        {
            await SeedResumeUsingSkillAsync("artificial intelligence");
        }

        await MigrateAsync(CatalogMigration);

        Assert.Equal(countAfterDown, await ScalarAsync("""SELECT count(*) FROM "Skills";"""));
        Assert.Equal(0L, await ScalarAsync(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'Skills' AND column_name = 'SearchAliases';
            """));
        Assert.Equal(V1CatalogHash, Hash((await ReadSkillsAsync())
            .Where(skill => skill.NormalizedName != "artificial intelligence")
            .ToList()));

        await MigrateAsync(LatestMigration);

        await using var dbContext = CreateContext();
        Assert.Equal(400, await dbContext.Skills.CountAsync());
        Assert.Equal(
            ["ai"],
            (await dbContext.Skills.AsNoTracking()
                .SingleAsync(item => item.NormalizedName == "artificial intelligence"))
                .SearchAliases);
    }

    private const string V1CatalogHash =
        "46c6c3a02b08b2f18c967c39524a2e2c27bbc328753d28546e479e7bf2c47c7f";

    private const string CurrentCatalogHash =
        "7119116ec6b633b0baf4a062ffd625004e55ca6dab95752aa9f61d7bf14951de";

    private static string Hash(IEnumerable<SkillCatalogItem> skills) =>
        SkillCatalogHashCalculator.Compute(skills);

    // Reads the hashed columns with SQL, so it works on every schema version.
    private async Task<List<SkillCatalogItem>> ReadSkillsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Id", "Name", "NormalizedName", "Category" FROM "Skills";""";
        await using var reader = await command.ExecuteReaderAsync();
        var skills = new List<SkillCatalogItem>();
        while (await reader.ReadAsync())
        {
            skills.Add(new SkillCatalogItem(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                (SkillCategory)reader.GetInt32(3)));
        }

        return skills;
    }

    private async Task<Guid> SeedResumeUsingSkillAsync(string normalizedName)
    {
        await using var dbContext = CreateContext();
        var skill = await dbContext.Skills.SingleAsync(item => item.NormalizedName == normalizedName);
        // Raw SQL, because this schema predates later columns of the User entity.
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            $"""
            INSERT INTO "Users" ("Id", "FirstName", "LastName", "Email", "PasswordHash", "IsActive", "IsEmailVerified", "CreatedAt")
            VALUES ('{userId}', 'Catalog', 'Test', 'catalog@example.com', 'integration-test-hash', true, false, now());
            """);
        var resume = new Resume { UserId = userId, Title = "Main", IsMasterResume = true };
        dbContext.AddRange(resume, new ResumeSkill { ResumeId = resume.Id, SkillId = skill.Id });
        await dbContext.SaveChangesAsync();
        return skill.Id;
    }

    private AppDbContext CreateContext() =>
        new TestDbContextFactory(_connectionString).CreateDbContext();

    private async Task MigrateAsync(string target)
    {
        await using var dbContext = CreateContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
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
