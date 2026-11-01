using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class JobPostingSkillProvenanceMigrationTests : IntegrationTestBase
{
    private const string PreviousMigration =
        "20260929084925_HardenJobPostingParseDraftLifecycle";
    private const string CurrentMigration =
        "20260929205811_AddJobPostingSkillProvenance";

    public JobPostingSkillProvenanceMigrationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Clean_database_has_provenance_schema_and_matching_snapshot()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        var applied = await context.Database.GetAppliedMigrationsAsync(
            cancellation.Token);
        var pending = await context.Database.GetPendingMigrationsAsync(
            cancellation.Token);

        Assert.Contains(CurrentMigration, applied);
        Assert.Empty(pending);
        Assert.False(context.Database.HasPendingModelChanges());
        await AssertSchemaAsync(Fixture.ConnectionString, cancellation.Token);
    }

    [Fact]
    public async Task Legacy_upgrade_classifies_rows_without_inventing_sources_or_changing_requirement_data()
    {
        using var cancellation = CreateCancellation();
        var databaseName = $"resumatch_provenance_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);

        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using (var context = factory.CreateDbContext())
            {
                await context.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration,
                    cancellation.Token);
            }

            var rows = await SeedLegacyRowsAsync(
                connectionString,
                cancellation.Token);

            await using (var context = factory.CreateDbContext())
            {
                await context.GetService<IMigrator>().MigrateAsync(
                    CurrentMigration,
                    cancellation.Token);
                Assert.False(context.Database.HasPendingModelChanges());
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellation.Token);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Id", "ProvenanceStatus", "SourceParseDraftId",
                       "SourceParseDraftSkillId", "RequirementType",
                       "ImportanceScore"
                FROM "JobPostingSkills"
                ORDER BY "Id";
                """;
            await using var reader = await command.ExecuteReaderAsync(
                cancellation.Token);
            var persisted = new List<LegacyResult>();
            while (await reader.ReadAsync(cancellation.Token))
            {
                persisted.Add(new LegacyResult(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }

            var manual = persisted.Single(item => item.Id == rows.ManualId);
            Assert.Equal((int)JobPostingSkillProvenanceStatus.Manual, manual.Status);
            Assert.Null(manual.SourceDraftId);
            Assert.Null(manual.SourceCandidateId);
            Assert.Equal(rows.ManualRequirementType, manual.RequirementType);
            Assert.Equal(rows.ManualImportance, manual.ImportanceScore);

            var legacyAi = persisted.Single(item => item.Id == rows.LegacyAiId);
            Assert.Equal(
                (int)JobPostingSkillProvenanceStatus.LegacyAiUnknown,
                legacyAi.Status);
            Assert.Null(legacyAi.SourceDraftId);
            Assert.Null(legacyAi.SourceCandidateId);
            Assert.Equal(rows.AiRequirementType, legacyAi.RequirementType);
            Assert.Equal(rows.AiImportance, legacyAi.ImportanceScore);
            await AssertSchemaAsync(connectionString, cancellation.Token);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Down_migration_removes_provenance_schema_without_deleting_skill_rows()
    {
        using var cancellation = CreateCancellation();
        var databaseName = $"resumatch_provenance_down_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        await CreateDatabaseAsync(databaseName, cancellation.Token);

        try
        {
            var factory = new TestDbContextFactory(connectionString);
            Guid requirementId;
            await using (var context = factory.CreateDbContext())
            {
                await context.GetService<IMigrator>().MigrateAsync(
                    CurrentMigration,
                    cancellation.Token);
                requirementId = await SeedCurrentManualRowAsync(
                    context,
                    cancellation.Token);
                await context.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration,
                    cancellation.Token);
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellation.Token);
            await using var rowCommand = connection.CreateCommand();
            rowCommand.CommandText =
                "SELECT COUNT(*) FROM \"JobPostingSkills\" WHERE \"Id\" = @id;";
            rowCommand.Parameters.AddWithValue("id", requirementId);
            Assert.Equal(
                1L,
                (long)(await rowCommand.ExecuteScalarAsync(cancellation.Token))!);

            await using var columnCommand = connection.CreateCommand();
            columnCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'JobPostingSkills'
                  AND column_name IN (
                      'ProvenanceStatus',
                      'SourceParseDraftId',
                      'SourceParseDraftSkillId',
                      'InvalidatedAt');
                """;
            Assert.Equal(
                0L,
                (long)(await columnCommand.ExecuteScalarAsync(
                    cancellation.Token))!);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    private async Task<Guid> SeedCurrentManualRowAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            FirstName = "Down",
            LastName = "Migration",
            Email = $"down-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            Description = "C# required.",
            Status = JobPostingStatus.Draft
        };
        // Raw SQL, because this schema predates later columns of the Skill entity.
        var skillId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Skills" ("Id", "Name", "NormalizedName", "Category", "CreatedAt")
            VALUES ({skillId}, 'C#', 'c#', {(int)SkillCategory.ProgrammingLanguage}, now());
            """,
            cancellationToken);
        var requirement = new JobPostingSkill
        {
            JobPostingId = posting.Id,
            SkillId = skillId,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 90,
            IsAiDetected = false,
            ProvenanceStatus = JobPostingSkillProvenanceStatus.Manual
        };
        context.AddRange(user, posting, requirement);
        await context.SaveChangesAsync(cancellationToken);
        return requirement.Id;
    }

    private async Task<LegacyRows> SeedLegacyRowsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var manualSkillId = Guid.NewGuid();
        var aiSkillId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var legacyAiId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(
            2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Users"
                ("Id", "FirstName", "LastName", "Email", "PasswordHash",
                 "IsEmailVerified", "IsActive", "CreatedAt")
            VALUES
                (@userId, 'Legacy', 'Owner', @email,
                 'integration-test-hash', FALSE, TRUE, @createdAt);

            INSERT INTO "JobPostings"
                ("Id", "UserId", "Title", "Description", "Status", "CreatedAt")
            VALUES
                (@postingId, @userId, 'Legacy Posting', 'Legacy source', 0,
                 @createdAt);

            INSERT INTO "Skills"
                ("Id", "Name", "NormalizedName", "Category", "CreatedAt")
            VALUES
                (@manualSkillId, 'C#', 'c#', 0, @createdAt),
                (@aiSkillId, 'Docker', 'docker', 4, @createdAt);

            INSERT INTO "JobPostingSkills"
                ("Id", "JobPostingId", "SkillId", "RequirementType",
                 "ImportanceScore", "IsAiDetected", "CreatedAt")
            VALUES
                (@manualId, @postingId, @manualSkillId, @manualType,
                 @manualImportance, FALSE, @createdAt),
                (@legacyAiId, @postingId, @aiSkillId, @aiType,
                 @aiImportance, TRUE, @createdAt);
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("postingId", postingId);
        command.Parameters.AddWithValue("manualSkillId", manualSkillId);
        command.Parameters.AddWithValue("aiSkillId", aiSkillId);
        command.Parameters.AddWithValue("manualId", manualId);
        command.Parameters.AddWithValue("legacyAiId", legacyAiId);
        command.Parameters.AddWithValue(
            "email",
            $"legacy-provenance-{Guid.NewGuid():N}@example.com");
        command.Parameters.AddWithValue("manualType", 2);
        command.Parameters.AddWithValue("manualImportance", 17);
        command.Parameters.AddWithValue("aiType", 0);
        command.Parameters.AddWithValue("aiImportance", 93);
        command.Parameters.AddWithValue("createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new LegacyRows(manualId, legacyAiId, 2, 17, 0, 93);
    }

    private static async Task AssertSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = '"JobPostingSkills"'::regclass
              AND conname IN (
                  'CK_JobPostingSkills_ProvenanceStatus',
                  'CK_JobPostingSkills_ProvenanceInvariant',
                  'FK_JobPostingSkills_SourceDraftSkill_Draft_Skill')
            ORDER BY conname;
            """;
        var constraints = new List<string>();
        await using (var reader = await constraintCommand.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                constraints.Add(reader.GetString(0));
            }
        }

        Assert.Equal(3, constraints.Count);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname IN (
                  'IX_JobPostingSkills_SourceParseDraftSkillId',
                  'IX_JobPostingSkills_JobPostingId_ProvenanceStatus',
                  'UX_JobPostingParseDraftSkills_ProvenanceIdentity')
            ORDER BY indexname;
            """;
        var indexes = new List<string>();
        await using var indexReader = await indexCommand.ExecuteReaderAsync(
            cancellationToken);
        while (await indexReader.ReadAsync(cancellationToken))
        {
            indexes.Add(indexReader.GetString(0));
        }

        Assert.Equal(3, indexes.Count);
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

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record LegacyRows(
        Guid ManualId,
        Guid LegacyAiId,
        int ManualRequirementType,
        int ManualImportance,
        int AiRequirementType,
        int AiImportance);

    private sealed record LegacyResult(
        Guid Id,
        int Status,
        Guid? SourceDraftId,
        Guid? SourceCandidateId,
        int RequirementType,
        int ImportanceScore);
}
