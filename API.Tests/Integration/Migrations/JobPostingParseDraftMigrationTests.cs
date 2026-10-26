using API.Tests.Integration.Infrastructure;
using Api.Application.JobParsing.Engine.Preprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class JobPostingParseDraftMigrationTests : IntegrationTestBase
{
    private const string PreviousMigration =
        "20260929075516_AddJobPostingParseDrafts";
    private const string CurrentMigration =
        "20260929084925_HardenJobPostingParseDraftLifecycle";

    public JobPostingParseDraftMigrationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CleanDatabase_HasDraftMigrationRuntimeModelAndExistingAtsTables()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync(
            cancellation.Token);
        var pending = await dbContext.Database.GetPendingMigrationsAsync(
            cancellation.Token);

        Assert.Contains(CurrentMigration, applied);
        Assert.Empty(pending);
        Assert.False(dbContext.Database.HasPendingModelChanges());
        await AssertTablesExistAsync(
            Fixture.ConnectionString,
            cancellation.Token);
        await AssertLifecycleSchemaAsync(
            Fixture.ConnectionString,
            cancellation.Token);
    }

    [Fact]
    public async Task PreviousDraftMigration_UpgradeBackfillsDraftAndPreservesAtsAnalysis()
    {
        using var cancellation = CreateCancellation();
        var databaseName = $"resumatch_drafts_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);

        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(connectionString);
            await using (var dbContext = factory.CreateDbContext())
            {
                await dbContext.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration,
                    cancellation.Token);
            }

            var legacy = await SeedLegacyDataAsync(
                connectionString,
                cancellation.Token);

            await using (var dbContext = factory.CreateDbContext())
            {
                await dbContext.GetService<IMigrator>().MigrateAsync(
                    CurrentMigration,
                    cancellation.Token);
                Assert.False(dbContext.Database.HasPendingModelChanges());
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellation.Token);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM \"ResumeAnalyses\" WHERE \"Id\" = @id;";
            command.Parameters.AddWithValue("id", legacy.AnalysisId);
            Assert.Equal(
                1L,
                (long)(await command.ExecuteScalarAsync(cancellation.Token))!);

            await using var draftCommand = connection.CreateCommand();
            draftCommand.CommandText =
                """
                SELECT "SourceTitle", "SourceInputHash", "SourceDescription"
                FROM "JobPostingParseDrafts"
                WHERE "Id" = @draftId;
                """;
            draftCommand.Parameters.AddWithValue("draftId", legacy.DraftId);
            await using var reader = await draftCommand.ExecuteReaderAsync(
                cancellation.Token);
            Assert.True(await reader.ReadAsync(cancellation.Token));
            Assert.Equal(legacy.Title, reader.GetString(0));
            Assert.Equal(
                SourceInputHashCalculator.Compute(
                    legacy.Title,
                    legacy.Description),
                reader.GetString(1));
            Assert.Equal(legacy.Description, reader.GetString(2));

            await AssertTablesExistAsync(connectionString, cancellation.Token);
            await AssertLifecycleSchemaAsync(connectionString, cancellation.Token);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
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

    private static async Task<LegacyData> SeedLegacyDataAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var jobPostingId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        const string title = "Legacy Backend Engineer";
        const string description = "C# and ASP.NET Core are required.";
        var createdAt = new DateTimeOffset(
            2026,
            9,
            29,
            12,
            0,
            0,
            TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Users"
                ("Id", "FirstName", "LastName", "Email", "PasswordHash",
                 "IsEmailVerified", "IsActive", "CreatedAt")
            VALUES
                (@userId, 'Migration', 'Owner', 'draft-migration@example.com',
                 'integration-test-hash', FALSE, TRUE, @createdAt);

            INSERT INTO "Resumes"
                ("Id", "UserId", "Title", "IsMasterResume", "IsArchived",
                 "CreatedAt")
            VALUES
                (@resumeId, @userId, 'Preserved Resume', TRUE, FALSE, @createdAt);

            INSERT INTO "ResumeAnalyses"
                ("Id", "UserId", "ResumeId", "Type", "AtsScore",
                 "StructureScore", "FormattingScore", "ContentScore",
                 "KeywordScore", "AnalyzedAt", "AnalyzerVersion", "CreatedAt")
            VALUES
                (@analysisId, @userId, @resumeId, 0, 75, 75, 75, 75, 75,
                 @createdAt, 'ats-v1', @createdAt);

            INSERT INTO "JobPostings"
                ("Id", "UserId", "Title", "Description", "Status", "CreatedAt")
            VALUES
                (@jobPostingId, @userId, @title, @description, 0, @createdAt);

            INSERT INTO "JobPostingParseDrafts"
                ("Id", "JobPostingId", "UserId", "SourceJobPostingVersion",
                 "SourceDescriptionHash", "SourceDescription", "ParserVersion",
                 "Status", "AttemptCount", "RequestedAt", "CreatedAt")
            VALUES
                (@draftId, @jobPostingId, @userId, 1,
                 @descriptionHash, @description, 'job-parser-v1',
                 2, 1, @createdAt, @createdAt);
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("resumeId", resumeId);
        command.Parameters.AddWithValue("analysisId", analysisId);
        command.Parameters.AddWithValue("jobPostingId", jobPostingId);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue(
            "descriptionHash",
            DescriptionHashCalculator.Compute(description));
        command.Parameters.AddWithValue("createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new LegacyData(analysisId, draftId, title, description);
    }

    private static async Task AssertLifecycleSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                """
                SELECT indexname
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'JobPostingParseDrafts'
                  AND indexname IN (
                      'UX_JobPostingParseDrafts_ActiveFingerprint',
                      'IX_JobPostingParseDrafts_PendingClaim',
                      'IX_JobPostingParseDrafts_ProcessingLease')
                ORDER BY indexname;
                """;
            var indexes = new List<string>();
            await using var reader = await indexCommand.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(reader.GetString(0));
            }

            Assert.Equal(3, indexes.Count);
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = '"JobPostingParseDrafts"'::regclass
              AND conname IN (
                  'CK_JobPostingParseDrafts_SourceTitle_NotBlank',
                  'CK_JobPostingParseDrafts_SourceInputHash',
                  'CK_JobPostingParseDrafts_SkillCatalogHash',
                  'CK_JobPostingParseDrafts_Status',
                  'CK_JobPostingParseDrafts_LeaseOwner_NotBlank',
                  'CK_JobPostingParseDrafts_ProcessingLease',
                  'CK_JobPostingParseDrafts_NextAttemptStatus')
            ORDER BY conname;
            """;
        var constraints = new List<string>();
        await using var constraintReader = await constraintCommand.ExecuteReaderAsync(
            cancellationToken);
        while (await constraintReader.ReadAsync(cancellationToken))
        {
            constraints.Add(constraintReader.GetString(0));
        }

        Assert.Equal(7, constraints.Count);
    }

    private static async Task AssertTablesExistAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN (
                  'JobPostingParseDrafts',
                  'JobPostingParseDraftSkills',
                  'JobPostingParseDraftSkillEvidences',
                  'ResumeAnalyses',
                  'AnalysisRecommendations')
            ORDER BY table_name;
            """;
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(5, tables.Count);
        Assert.Contains("JobPostingParseDrafts", tables);
        Assert.Contains("JobPostingParseDraftSkills", tables);
        Assert.Contains("JobPostingParseDraftSkillEvidences", tables);
        Assert.Contains("ResumeAnalyses", tables);
        Assert.Contains("AnalysisRecommendations", tables);
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record LegacyData(
        Guid AnalysisId,
        Guid DraftId,
        string Title,
        string Description);
}
