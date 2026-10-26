using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class AnalysisSourceProvenanceMigrationTests : IntegrationTestBase
{
    private const string PreviousMigration =
        "20260929205811_AddJobPostingSkillProvenance";
    private const string CurrentMigration =
        "20261001110910_AddAnalysisSourceProvenance";

    public AnalysisSourceProvenanceMigrationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Clean_database_has_source_provenance_schema_and_no_pending_migrations()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        Assert.Contains(
            CurrentMigration,
            await context.Database.GetAppliedMigrationsAsync(cancellation.Token));
        Assert.Empty(await context.Database
            .GetPendingMigrationsAsync(cancellation.Token));
        await AssertSchemaAsync(Fixture.ConnectionString, cancellation.Token);
    }

    [Fact]
    public async Task Legacy_upgrade_preserves_analysis_aggregate_and_leaves_provenance_unknown()
    {
        using var cancellation = CreateCancellation();
        var databaseName = $"resumatch_analysis_source_{Guid.NewGuid():N}";
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

            var seed = await SeedLegacyAggregateAsync(
                connectionString,
                cancellation.Token);

            await using (var context = factory.CreateDbContext())
            {
                await context.GetService<IMigrator>().MigrateAsync(
                    CurrentMigration,
                    cancellation.Token);
            }

            await AssertLegacyAggregateAsync(
                connectionString,
                seed,
                cancellation.Token);
            await AssertSchemaAsync(connectionString, cancellation.Token);
            await AssertConstraintsAsync(
                connectionString,
                seed,
                cancellation.Token);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    private async Task<LegacySeed> SeedLegacyAggregateAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var seed = new LegacySeed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var createdAt = new DateTimeOffset(
            2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Users"
                ("Id", "FirstName", "LastName", "Email", "PasswordHash",
                 "IsEmailVerified", "IsActive", "CreatedAt")
            VALUES
                (@userId, 'Legacy', 'Analysis', @email, 'test-hash',
                 FALSE, TRUE, @createdAt);

            INSERT INTO "Resumes"
                ("Id", "UserId", "Title", "IsMasterResume", "IsArchived",
                 "CreatedAt")
            VALUES
                (@resumeId, @userId, 'Legacy Resume', TRUE, FALSE, @createdAt);

            INSERT INTO "JobPostings"
                ("Id", "UserId", "Title", "Description", "Status", "CreatedAt")
            VALUES
                (@postingId, @userId, 'Legacy Job', 'Legacy description.', 1,
                 @createdAt);

            INSERT INTO "ResumeAnalyses"
                ("Id", "UserId", "ResumeId", "Type", "AtsScore",
                 "StructureScore", "FormattingScore", "ContentScore",
                 "KeywordScore", "AnalyzedAt", "AnalyzerVersion", "CreatedAt")
            VALUES
                (@atsId, @userId, @resumeId, 0, 51.25, 50, 60, 45, 55,
                 @createdAt, 'ats-v1', @createdAt);

            INSERT INTO "ResumeAnalyses"
                ("Id", "UserId", "ResumeId", "JobPostingId",
                 "JobPostingUserId", "Type", "AtsScore", "JobMatchScore",
                 "StructureScore", "FormattingScore", "ContentScore",
                 "KeywordScore", "AnalyzedAt", "AnalyzerVersion", "CreatedAt")
            VALUES
                (@jobMatchId, @userId, @resumeId, @postingId, @userId, 1,
                 73.50, 42.25, 70, 80, 65, 75, @createdAt, 'ats-v1', @createdAt);

            INSERT INTO "AnalysisIssues"
                ("Id", "ResumeAnalysisId", "Type", "Severity", "Code",
                 "Title", "Description", "CreatedAt")
            VALUES
                (@issueId, @jobMatchId, 3, 1, 'LEGACY.TEST', 'Legacy issue',
                 'Legacy issue description.', @createdAt);

            INSERT INTO "KeywordMatches"
                ("Id", "ResumeAnalysisId", "Keyword", "Status", "Weight",
                 "CreatedAt")
            VALUES
                (@keywordId, @jobMatchId, 'Docker', 1, 100, @createdAt);

            INSERT INTO "AnalysisRecommendations"
                ("Id", "ResumeAnalysisId", "RuleCode", "SortOrder", "Type",
                 "Priority", "Title", "Description", "IsAiGenerated",
                 "Status", "CreatedAt")
            VALUES
                (@recommendationId, @jobMatchId, 'LEGACY.TEST', 0, 0, 1,
                 'Legacy recommendation', 'Legacy recommendation description.',
                 FALSE, 0, @createdAt);
            """;
        command.Parameters.AddWithValue("userId", seed.UserId);
        command.Parameters.AddWithValue("resumeId", seed.ResumeId);
        command.Parameters.AddWithValue("postingId", seed.JobPostingId);
        command.Parameters.AddWithValue("atsId", seed.AtsAnalysisId);
        command.Parameters.AddWithValue("jobMatchId", seed.JobMatchAnalysisId);
        command.Parameters.AddWithValue("issueId", seed.IssueId);
        command.Parameters.AddWithValue("keywordId", seed.KeywordId);
        command.Parameters.AddWithValue("recommendationId", seed.RecommendationId);
        command.Parameters.AddWithValue(
            "email",
            $"legacy-source-{Guid.NewGuid():N}@example.com");
        command.Parameters.AddWithValue("createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return seed;
    }

    private static async Task AssertLegacyAggregateAsync(
        string connectionString,
        LegacySeed seed,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT "Id", "AtsScore", "JobMatchScore", "AnalyzerVersion",
                       "SourceJobPostingVersion", "SourceJobPostingInputHash",
                       "SourceJobPostingSkillSetHash"
                FROM "ResumeAnalyses"
                ORDER BY "Id";
                """;
            var rows = new Dictionary<Guid, LegacyAnalysisRow>();
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(
                    reader.GetGuid(0),
                    new LegacyAnalysisRow(
                        reader.GetDecimal(1),
                        reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                        reader.GetString(3),
                        reader.IsDBNull(4),
                        reader.IsDBNull(5),
                        reader.IsDBNull(6)));
            }

            Assert.Equal(2, rows.Count);
            Assert.Equal(51.25m, rows[seed.AtsAnalysisId].AtsScore);
            Assert.Null(rows[seed.AtsAnalysisId].JobMatchScore);
            Assert.Equal(73.50m, rows[seed.JobMatchAnalysisId].AtsScore);
            Assert.Equal(42.25m, rows[seed.JobMatchAnalysisId].JobMatchScore);
            Assert.All(rows.Values, row =>
            {
                Assert.Equal("ats-v1", row.AnalyzerVersion);
                Assert.True(row.VersionIsNull);
                Assert.True(row.InputHashIsNull);
                Assert.True(row.SkillHashIsNull);
            });
        }

        await using var childCommand = connection.CreateCommand();
        childCommand.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM "AnalysisIssues"),
                (SELECT COUNT(*) FROM "KeywordMatches"),
                (SELECT COUNT(*) FROM "AnalysisRecommendations");
            """;
        await using var childReader = await childCommand.ExecuteReaderAsync(
            cancellationToken);
        Assert.True(await childReader.ReadAsync(cancellationToken));
        Assert.Equal(1L, childReader.GetInt64(0));
        Assert.Equal(1L, childReader.GetInt64(1));
        Assert.Equal(1L, childReader.GetInt64(2));
    }

    private static async Task AssertSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT conname
            FROM pg_constraint
            WHERE conname IN (
                'CK_ResumeAnalyses_SourceJobPostingVersion',
                'CK_ResumeAnalyses_SourceJobPostingInputHash',
                'CK_ResumeAnalyses_SourceJobPostingSkillSetHash',
                'CK_ResumeAnalyses_SourceProvenanceCompleteness')
            ORDER BY conname;
            """;
        var constraints = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            constraints.Add(reader.GetString(0));
        }

        Assert.Equal(4, constraints.Count);
    }

    private static async Task AssertConstraintsAsync(
        string connectionString,
        LegacySeed seed,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await AssertUpdateRejectedAsync(
            connection,
            seed.AtsAnalysisId,
            "\"SourceJobPostingVersion\" = 1",
            "CK_ResumeAnalyses_SourceProvenanceCompleteness",
            cancellationToken);
        await AssertUpdateRejectedAsync(
            connection,
            seed.AtsAnalysisId,
            "\"SourceJobPostingVersion\" = 0, " +
            "\"SourceJobPostingInputHash\" = repeat('a', 64), " +
            "\"SourceJobPostingSkillSetHash\" = repeat('b', 64)",
            "CK_ResumeAnalyses_SourceJobPostingVersion",
            cancellationToken);
        await AssertUpdateRejectedAsync(
            connection,
            seed.AtsAnalysisId,
            "\"SourceJobPostingVersion\" = 1, " +
            "\"SourceJobPostingInputHash\" = repeat('A', 64), " +
            "\"SourceJobPostingSkillSetHash\" = repeat('b', 64)",
            "CK_ResumeAnalyses_SourceJobPostingInputHash",
            cancellationToken);
    }

    private static async Task AssertUpdateRejectedAsync(
        NpgsqlConnection connection,
        Guid analysisId,
        string assignments,
        string expectedConstraint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE \"ResumeAnalyses\" SET {assignments} WHERE \"Id\" = @id;";
        command.Parameters.AddWithValue("id", analysisId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
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
        new(TimeSpan.FromSeconds(90));

    private sealed record LegacySeed(
        Guid UserId,
        Guid ResumeId,
        Guid JobPostingId,
        Guid AtsAnalysisId,
        Guid JobMatchAnalysisId,
        Guid IssueId,
        Guid KeywordId,
        Guid RecommendationId);

    private sealed record LegacyAnalysisRow(
        decimal AtsScore,
        decimal? JobMatchScore,
        string AnalyzerVersion,
        bool VersionIsNull,
        bool InputHashIsNull,
        bool SkillHashIsNull);
}
