using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace API.Tests.Integration.Migrations;

public sealed class AnalysisPersistenceMigrationTests : IntegrationTestBase
{
    private const string PreviousMigration =
        "20260927105531_AddJobApplicationOptimisticConcurrency";
    private const string CurrentMigration =
        "20260928220558_HardenAnalysisPersistenceMetadata";

    public AnalysisPersistenceMigrationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CleanDatabase_HasHardeningMigrationAndRuntimeModelApplied()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();

        var applied = await dbContext.Database
            .GetAppliedMigrationsAsync(cancellation.Token);
        var pending = await dbContext.Database
            .GetPendingMigrationsAsync(cancellation.Token);

        Assert.Contains(CurrentMigration, applied);
        Assert.Empty(pending);
        await AssertHardeningSchemaAsync(
            Fixture.ConnectionString,
            cancellation.Token);
    }

    [Fact]
    public async Task LegacyDatabase_UpgradeBackfillsOnlyUnknownVersionsAndPreservesRows()
    {
        using var cancellation = CreateCancellation();
        var databaseName = $"resumatch_legacy_{Guid.NewGuid():N}";
        var temporaryConnectionString = BuildConnectionString(databaseName);

        await CreateDatabaseAsync(databaseName, cancellation.Token);
        try
        {
            var factory = new TestDbContextFactory(temporaryConnectionString);
            await using (var dbContext = factory.CreateDbContext())
            {
                var migrator = dbContext.GetService<IMigrator>();
                await migrator.MigrateAsync(
                    PreviousMigration,
                    cancellation.Token);
            }

            var seeded = await SeedLegacyRowsAsync(
                temporaryConnectionString,
                cancellation.Token);

            await using (var dbContext = factory.CreateDbContext())
            {
                var migrator = dbContext.GetService<IMigrator>();
                await migrator.MigrateAsync(
                    CurrentMigration,
                    cancellation.Token);
            }

            await AssertLegacyUpgradeAsync(
                temporaryConnectionString,
                seeded,
                cancellation.Token);
            await AssertHardeningSchemaAsync(
                temporaryConnectionString,
                cancellation.Token);
            await AssertConstraintsAsync(
                temporaryConnectionString,
                seeded,
                cancellation.Token);
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

    private static async Task<SeededIds> SeedLegacyRowsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var nullVersionAnalysisId = Guid.NewGuid();
        var whitespaceVersionAnalysisId = Guid.NewGuid();
        var validVersionAnalysisId = Guid.NewGuid();
        var recommendationIds = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };
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
                (@userId, 'Legacy', 'User', 'legacy-migration@example.com',
                 'legacy-test-hash', FALSE, TRUE, @createdAt);

            INSERT INTO "Resumes"
                ("Id", "UserId", "Title", "IsMasterResume", "IsArchived",
                 "CreatedAt")
            VALUES
                (@resumeId, @userId, 'Legacy Resume', TRUE, FALSE, @createdAt);

            INSERT INTO "ResumeAnalyses"
                ("Id", "UserId", "ResumeId", "Type", "AtsScore",
                 "StructureScore", "FormattingScore", "ContentScore",
                 "KeywordScore", "AnalyzedAt", "AnalyzerVersion", "CreatedAt")
            VALUES
                (@nullAnalysisId, @userId, @resumeId, 0, 50, 50, 50, 50, 50,
                 @createdAt, NULL, @createdAt),
                (@whitespaceAnalysisId, @userId, @resumeId, 0, 50, 50, 50, 50, 50,
                 @createdAt, E' \t\n ', @createdAt),
                (@validAnalysisId, @userId, @resumeId, 0, 50, 50, 50, 50, 50,
                 @createdAt, 'ats-legacy-verified', @createdAt);

            INSERT INTO "AnalysisRecommendations"
                ("Id", "ResumeAnalysisId", "Type", "Priority", "Title",
                 "Description", "IsAiGenerated", "Status", "CreatedAt")
            VALUES
                (@recommendation0, @nullAnalysisId, 7, 1, 'Legacy recommendation 0',
                 'Legacy recommendation without metadata.', FALSE, 0, @createdAt),
                (@recommendation1, @whitespaceAnalysisId, 7, 1, 'Legacy recommendation 1',
                 'Legacy recommendation without metadata.', FALSE, 0, @createdAt),
                (@recommendation2, @validAnalysisId, 7, 1, 'Legacy recommendation 2',
                 'Legacy recommendation without metadata.', FALSE, 0, @createdAt);
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("resumeId", resumeId);
        command.Parameters.AddWithValue("nullAnalysisId", nullVersionAnalysisId);
        command.Parameters.AddWithValue(
            "whitespaceAnalysisId",
            whitespaceVersionAnalysisId);
        command.Parameters.AddWithValue("validAnalysisId", validVersionAnalysisId);
        command.Parameters.AddWithValue("recommendation0", recommendationIds[0]);
        command.Parameters.AddWithValue("recommendation1", recommendationIds[1]);
        command.Parameters.AddWithValue("recommendation2", recommendationIds[2]);
        command.Parameters.AddWithValue("createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new SeededIds(
            userId,
            resumeId,
            nullVersionAnalysisId,
            whitespaceVersionAnalysisId,
            validVersionAnalysisId,
            recommendationIds);
    }

    private static async Task AssertLegacyUpgradeAsync(
        string connectionString,
        SeededIds seeded,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText =
                """
                SELECT
                    (SELECT COUNT(*) FROM "ResumeAnalyses"),
                    (SELECT COUNT(*) FROM "AnalysisRecommendations");
                """;
            await using var reader = await countCommand.ExecuteReaderAsync(
                cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal(3L, reader.GetInt64(0));
            Assert.Equal(3L, reader.GetInt64(1));
        }

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText =
                """
                SELECT "Id", "AnalyzerVersion"
                FROM "ResumeAnalyses"
                ORDER BY "Id";
                """;
            var versions = new Dictionary<Guid, string>();
            await using var reader = await versionCommand.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                versions.Add(reader.GetGuid(0), reader.GetString(1));
            }

            Assert.Equal(
                "legacy-unknown",
                versions[seeded.NullVersionAnalysisId]);
            Assert.Equal(
                "legacy-unknown",
                versions[seeded.WhitespaceVersionAnalysisId]);
            Assert.Equal(
                "ats-legacy-verified",
                versions[seeded.ValidVersionAnalysisId]);
        }

        await using var metadataCommand = connection.CreateCommand();
        metadataCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM "AnalysisRecommendations"
            WHERE "RuleCode" IS NULL
              AND "SortOrder" IS NULL;
            """;
        Assert.Equal(
            3L,
            (long)(await metadataCommand.ExecuteScalarAsync(cancellationToken))!);
    }

    private static async Task AssertHardeningSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.CommandText =
                """
                SELECT is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'ResumeAnalyses'
                  AND column_name = 'AnalyzerVersion';
                """;
            await using var reader = await columnCommand.ExecuteReaderAsync(
                cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal("NO", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            """
            SELECT conname
            FROM pg_constraint
            WHERE conname IN (
                'CK_ResumeAnalyses_AnalyzerVersion_NotBlank',
                'CK_AnalysisRecommendations_SortOrder',
                'FK_ResumeAnalyses_Resumes_ResumeId_UserId')
            ORDER BY conname;
            """;
        var constraints = new List<string>();
        await using var constraintReader = await constraintCommand.ExecuteReaderAsync(
            cancellationToken);
        while (await constraintReader.ReadAsync(cancellationToken))
        {
            constraints.Add(constraintReader.GetString(0));
        }

        Assert.Contains("CK_ResumeAnalyses_AnalyzerVersion_NotBlank", constraints);
        Assert.Contains("CK_AnalysisRecommendations_SortOrder", constraints);
        Assert.Contains("FK_ResumeAnalyses_Resumes_ResumeId_UserId", constraints);
    }

    private static async Task AssertConstraintsAsync(
        string connectionString,
        SeededIds seeded,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await AssertAnalysisVersionRejectedAsync(
            connection,
            seeded,
            Guid.NewGuid(),
            null,
            PostgresErrorCodes.NotNullViolation,
            null,
            cancellationToken);
        await AssertAnalysisVersionRejectedAsync(
            connection,
            seeded,
            Guid.NewGuid(),
            " \t\n ",
            PostgresErrorCodes.CheckViolation,
            "CK_ResumeAnalyses_AnalyzerVersion_NotBlank",
            cancellationToken);

        await UpdateRecommendationSortOrderAsync(
            connection,
            seeded.RecommendationIds[0],
            0,
            cancellationToken);
        await AssertRecommendationInsertRejectedAsync(
            connection,
            seeded.NullVersionAnalysisId,
            Guid.NewGuid(),
            0,
            PostgresErrorCodes.UniqueViolation,
            "IX_AnalysisRecommendations_ResumeAnalysisId_SortOrder",
            cancellationToken);
        await AssertRecommendationInsertRejectedAsync(
            connection,
            seeded.WhitespaceVersionAnalysisId,
            Guid.NewGuid(),
            -1,
            PostgresErrorCodes.CheckViolation,
            "CK_AnalysisRecommendations_SortOrder",
            cancellationToken);

        await UpdateRecommendationSortOrderAsync(
            connection,
            seeded.RecommendationIds[1],
            0,
            cancellationToken);

        await AssertRuleCodeLengthRejectedAsync(
            connection,
            seeded.RecommendationIds[2],
            cancellationToken);
    }

    private static async Task AssertRuleCodeLengthRejectedAsync(
        NpgsqlConnection connection,
        Guid recommendationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE "AnalysisRecommendations"
            SET "RuleCode" = @ruleCode
            WHERE "Id" = @id;
            """;
        command.Parameters.AddWithValue("ruleCode", new string('X', 101));
        command.Parameters.AddWithValue("id", recommendationId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(
            PostgresErrorCodes.StringDataRightTruncation,
            exception.SqlState);
    }

    private static async Task AssertAnalysisVersionRejectedAsync(
        NpgsqlConnection connection,
        SeededIds seeded,
        Guid analysisId,
        string? analyzerVersion,
        string expectedSqlState,
        string? expectedConstraint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "ResumeAnalyses"
                ("Id", "UserId", "ResumeId", "Type", "AtsScore",
                 "StructureScore", "FormattingScore", "ContentScore",
                 "KeywordScore", "AnalyzedAt", "AnalyzerVersion", "CreatedAt")
            VALUES
                (@id, @userId, @resumeId, 0, 50, 50, 50, 50, 50,
                 @createdAt, @analyzerVersion, @createdAt);
            """;
        command.Parameters.AddWithValue("id", analysisId);
        command.Parameters.AddWithValue("userId", seeded.UserId);
        command.Parameters.AddWithValue("resumeId", seeded.ResumeId);
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue(
            "analyzerVersion",
            analyzerVersion is null ? DBNull.Value : analyzerVersion);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(expectedSqlState, exception.SqlState);
        if (expectedConstraint is not null)
        {
            Assert.Equal(expectedConstraint, exception.ConstraintName);
        }
        else
        {
            Assert.Equal("AnalyzerVersion", exception.ColumnName);
        }
    }

    private static async Task UpdateRecommendationSortOrderAsync(
        NpgsqlConnection connection,
        Guid recommendationId,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE "AnalysisRecommendations"
            SET "RuleCode" = 'ATS.TEST.001',
                "SortOrder" = @sortOrder
            WHERE "Id" = @id;
            """;
        command.Parameters.AddWithValue("sortOrder", sortOrder);
        command.Parameters.AddWithValue("id", recommendationId);
        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task AssertRecommendationInsertRejectedAsync(
        NpgsqlConnection connection,
        Guid analysisId,
        Guid recommendationId,
        int sortOrder,
        string expectedSqlState,
        string expectedConstraint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "AnalysisRecommendations"
                ("Id", "ResumeAnalysisId", "RuleCode", "SortOrder", "Type",
                 "Priority", "Title", "Description", "IsAiGenerated",
                 "Status", "CreatedAt")
            VALUES
                (@id, @analysisId, 'ATS.TEST.002', @sortOrder, 7, 1,
                 'Constraint test', 'Constraint test recommendation.',
                 FALSE, 0, @createdAt);
            """;
        command.Parameters.AddWithValue("id", recommendationId);
        command.Parameters.AddWithValue("analysisId", analysisId);
        command.Parameters.AddWithValue("sortOrder", sortOrder);
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(expectedSqlState, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static CancellationTokenSource CreateCancellation()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(90));
    }

    private sealed record SeededIds(
        Guid UserId,
        Guid ResumeId,
        Guid NullVersionAnalysisId,
        Guid WhitespaceVersionAnalysisId,
        Guid ValidVersionAnalysisId,
        IReadOnlyList<Guid> RecommendationIds);
}
