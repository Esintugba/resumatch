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
    public async Task Up_InsertsExactlyTheCatalogFile()
    {
        await MigrateAsync(CatalogMigration);

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
        });
        var hash = SkillCatalogHashCalculator.Compute(skills.Select(skill => new SkillCatalogItem(
            skill.Id,
            skill.Name,
            skill.NormalizedName,
            skill.Category)));
        Assert.True(hash == ExpectedCatalogHash, $"Catalog hash is {hash}.");
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

        await MigrateAsync(CatalogMigration);

        await using var dbContext = CreateContext();
        var skill = await dbContext.Skills.AsNoTracking()
            .SingleAsync(item => item.NormalizedName == "teamwork");
        Assert.Equal(existingId, skill.Id);
        Assert.Equal(SkillCategory.Other, skill.Category);
        Assert.Equal("Takım çalışması", skill.NameTr);
        Assert.Equal(399, await dbContext.Skills.CountAsync());
    }

    [Fact]
    public async Task UpDownUp_RemovesOnlyUnusedRowsAndRestoresTheCatalog()
    {
        await MigrateAsync(CatalogMigration);
        var skillId = await SeedResumeUsingSkillAsync("react");

        await MigrateAsync(PreviousMigration);

        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM "Skills";"""));
        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM "ResumeSkills";"""));
        Assert.Equal(0L, await ScalarAsync(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE column_name IN ('NameTr', 'NormalizedNameTr', 'UnlistedSkillsNote');
            """));

        await MigrateAsync(CatalogMigration);

        await using var dbContext = CreateContext();
        Assert.Equal(399, await dbContext.Skills.CountAsync());
        Assert.Equal(
            skillId,
            (await dbContext.ResumeSkills.AsNoTracking().SingleAsync()).SkillId);
        Assert.Equal(
            "Frontend",
            (await dbContext.Skills.AsNoTracking().SingleAsync(item => item.Id == skillId))
                .Category.ToString());
    }

    private const string ExpectedCatalogHash =
        "46c6c3a02b08b2f18c967c39524a2e2c27bbc328753d28546e479e7bf2c47c7f";

    private async Task<Guid> SeedResumeUsingSkillAsync(string normalizedName)
    {
        await using var dbContext = CreateContext();
        var skill = await dbContext.Skills.SingleAsync(item => item.NormalizedName == normalizedName);
        var user = new User
        {
            FirstName = "Catalog",
            LastName = "Test",
            Email = "catalog@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var resume = new Resume { UserId = user.Id, Title = "Main", IsMasterResume = true };
        dbContext.AddRange(user, resume, new ResumeSkill { ResumeId = resume.Id, SkillId = skill.Id });
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
