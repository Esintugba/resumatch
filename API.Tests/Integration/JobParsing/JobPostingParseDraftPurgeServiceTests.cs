using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Application.Common.Interfaces;
using Api.Data;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftPurgeServiceTests(PostgreSqlFixture fixture) :
    IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Pending)]
    [InlineData(JobPostingParseDraftStatus.Processing)]
    [InlineData(JobPostingParseDraftStatus.Ready)]
    public async Task PurgeBatchAsync_Nonterminal_draft_is_never_purged(
        JobPostingParseDraftStatus status)
    {
        var seeded = await SeedDraftAsync(status, Now.AddDays(-90));

        var purged = await PurgeAsync();

        Assert.Equal(0, purged);
        await AssertDraftExistsAsync(seeded.DraftId, true);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task PurgeBatchAsync_Recent_terminal_draft_is_retained(
        JobPostingParseDraftStatus status)
    {
        var seeded = await SeedDraftAsync(status, Now.AddDays(-29));

        var purged = await PurgeAsync();

        Assert.Equal(0, purged);
        await AssertDraftExistsAsync(seeded.DraftId, true);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task PurgeBatchAsync_Old_unreferenced_terminal_draft_is_purged(
        JobPostingParseDraftStatus status)
    {
        var seeded = await SeedDraftAsync(status, Now.AddDays(-31));

        var purged = await PurgeAsync();

        Assert.Equal(1, purged);
        await AssertDraftExistsAsync(seeded.DraftId, false);
    }

    [Theory]
    [InlineData(JobPostingSkillProvenanceStatus.AiActive)]
    [InlineData(JobPostingSkillProvenanceStatus.AiStale)]
    [InlineData(JobPostingSkillProvenanceStatus.AiManuallyAdopted)]
    public async Task PurgeBatchAsync_Referenced_provenance_is_retained(
        JobPostingSkillProvenanceStatus provenanceStatus)
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Confirmed,
            Now.AddDays(-90),
            withCandidate: true);
        await SeedRequirementAsync(seeded, provenanceStatus);

        var purged = await PurgeAsync();

        Assert.Equal(0, purged);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.True(await verification.JobPostingParseDrafts.AnyAsync(
            item => item.Id == seeded.DraftId));
        Assert.True(await verification.JobPostingParseDraftSkills.AnyAsync(
            item => item.Id == seeded.CandidateId));
        Assert.True(await verification.JobPostingSkills.AnyAsync(
            item => item.SourceParseDraftId == seeded.DraftId));
    }

    [Theory]
    [InlineData(JobPostingSkillProvenanceStatus.Manual)]
    [InlineData(JobPostingSkillProvenanceStatus.LegacyAiUnknown)]
    public async Task PurgeBatchAsync_Sourceless_requirement_does_not_block_and_is_not_deleted(
        JobPostingSkillProvenanceStatus status)
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed,
            Now.AddDays(-90));
        Guid requirementId;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var requirement = new JobPostingSkill
            {
                JobPostingId = seeded.JobPostingId,
                SkillId = seeded.SkillId,
                RequirementType = JobSkillRequirementType.Required,
                ImportanceScore = 75,
                IsAiDetected = status == JobPostingSkillProvenanceStatus.LegacyAiUnknown,
                ProvenanceStatus = status
            };
            requirementId = requirement.Id;
            context.JobPostingSkills.Add(requirement);
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await PurgeAsync());

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await verification.JobPostingParseDrafts.AnyAsync(
            item => item.Id == seeded.DraftId));
        Assert.True(await verification.JobPostingSkills.AnyAsync(
            item => item.Id == requirementId));
    }

    [Fact]
    public async Task PurgeBatchAsync_Terminal_row_without_reliable_timestamp_is_retained()
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed,
            Now.AddDays(-90));
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var draft = await context.JobPostingParseDrafts.SingleAsync(
                item => item.Id == seeded.DraftId);
            draft.CompletedAt = null;
            await context.SaveChangesAsync();
        }

        Assert.Equal(0, await PurgeAsync());
        await AssertDraftExistsAsync(seeded.DraftId, true);
    }

    [Fact]
    public async Task PurgeBatchAsync_Deletes_candidate_and_evidence_by_database_cascade()
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed,
            Now.AddDays(-90),
            withCandidate: true,
            withEvidence: true);

        Assert.Equal(1, await PurgeAsync());

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await verification.JobPostingParseDrafts.AnyAsync(
            item => item.Id == seeded.DraftId));
        Assert.False(await verification.JobPostingParseDraftSkills.AnyAsync(
            item => item.JobPostingParseDraftId == seeded.DraftId));
        Assert.False(await verification.JobPostingParseDraftSkillEvidences.AnyAsync(
            item => item.JobPostingParseDraftSkillId == seeded.CandidateId));
    }

    [Fact]
    public async Task PurgeBatchAsync_Enforces_batch_size_and_oldest_first_ordering()
    {
        var oldest = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed, Now.AddDays(-60));
        var middle = await SeedDraftAsync(
            JobPostingParseDraftStatus.Cancelled, Now.AddDays(-50));
        var newest = await SeedDraftAsync(
            JobPostingParseDraftStatus.Expired, Now.AddDays(-40));

        var purged = await PurgeAsync(batchSize: 2);

        Assert.Equal(2, purged);
        await AssertDraftExistsAsync(oldest.DraftId, false);
        await AssertDraftExistsAsync(middle.DraftId, false);
        await AssertDraftExistsAsync(newest.DraftId, true);
    }

    [Fact]
    public async Task PurgeBatchAsync_Two_replicas_delete_each_draft_once()
    {
        for (var index = 0; index < 6; index++)
        {
            await SeedDraftAsync(
                JobPostingParseDraftStatus.Failed,
                Now.AddDays(-60).AddMinutes(index));
        }

        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var first = CreateService(firstContext, batchSize: 3);
        var second = CreateService(secondContext, batchSize: 3);

        var results = await Task.WhenAll(
            first.PurgeBatchAsync(CancellationToken.None),
            second.PurgeBatchAsync(CancellationToken.None));

        Assert.Equal(6, results.Sum());
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(0, await verification.JobPostingParseDrafts.CountAsync());
    }

    [Fact]
    public async Task PurgeBatchAsync_Cancelled_token_does_not_mutate_database()
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed, Now.AddDays(-60));
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PurgeBatchAsync(cancellation.Token));

        await AssertDraftExistsAsync(seeded.DraftId, true);
    }

    [Fact]
    public async Task PurgeBatchAsync_Does_not_modify_resume_analysis_history()
    {
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Failed, Now.AddDays(-60));
        Guid analysisId;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var resume = new Resume
            {
                UserId = seeded.UserId,
                Title = "Historical resume"
            };
            var analysis = new ResumeAnalysis
            {
                UserId = seeded.UserId,
                Resume = resume,
                Type = AnalysisType.AtsOnly,
                AtsScore = 50,
                StructureScore = 50,
                FormattingScore = 50,
                ContentScore = 50,
                KeywordScore = 50,
                AnalyzedAt = Now.AddDays(-100),
                AnalyzerVersion = "ats-v1"
            };
            analysisId = analysis.Id;
            context.Add(analysis);
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await PurgeAsync());

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(item => item.Id == analysisId);
        Assert.Equal(50, persisted.AtsScore);
        Assert.Equal("ats-v1", persisted.AnalyzerVersion);
    }

    [Fact]
    public async Task PurgeBatchAsync_Concurrent_provenance_insert_is_preserved_by_fk_fence()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Confirmed,
            Now.AddDays(-90),
            withCandidate: true);

        await using var insertContext = Fixture.DbContextFactory.CreateDbContext();
        await using var insertTransaction = await insertContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation.Token);
        insertContext.JobPostingSkills.Add(CreateRequirement(
            seeded,
            JobPostingSkillProvenanceStatus.AiActive));
        await insertContext.SaveChangesAsync(cancellation.Token);

        await using var purgeContext = Fixture.DbContextFactory.CreateDbContext();
        var purgeResult = await CreateService(purgeContext)
            .PurgeBatchAsync(cancellation.Token);
        Assert.Equal(0, purgeResult);
        await insertTransaction.CommitAsync(cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.True(await verification.JobPostingParseDrafts.AnyAsync(
            item => item.Id == seeded.DraftId, cancellation.Token));
        Assert.True(await verification.JobPostingSkills.AnyAsync(
            item => item.SourceParseDraftId == seeded.DraftId,
            cancellation.Token));
    }

    [Fact]
    public async Task PurgeBatchAsync_Confirmation_race_preserves_ready_draft_and_result()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Ready,
            Now.AddDays(-90));
        uint draftVersion;
        await using (var updateContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var draft = await updateContext.JobPostingParseDrafts.SingleAsync(
                item => item.Id == seeded.DraftId,
                cancellation.Token);
            draft.ExpiresAt = Now.AddHours(1);
            await updateContext.SaveChangesAsync(cancellation.Token);
            draftVersion = draft.Version;
        }

        await using var blocker = new NpgsqlConnection(Fixture.ConnectionString);
        await blocker.OpenAsync(cancellation.Token);
        await using var blockerTransaction = await blocker.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellation.Token);
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText =
                "SELECT 1 FROM \"JobPostings\" WHERE \"Id\" = @id FOR UPDATE;";
            lockCommand.Parameters.AddWithValue("id", seeded.JobPostingId);
            await lockCommand.ExecuteScalarAsync(cancellation.Token);
        }

        await using var confirmationContext = Fixture.DbContextFactory.CreateDbContext();
        var confirmationService = CreateDraftService(
            confirmationContext,
            seeded.UserId);
        var confirmationTask = confirmationService.ConfirmDraftAsync(
            seeded.JobPostingId,
            seeded.DraftId,
            draftVersion,
            new ConfirmJobPostingParseDraftRequest(),
            cancellation.Token);
        await WaitForBlockedQueryAsync(
            "SELECT posting.\"Title\", posting.\"Description\", posting.xmin",
            cancellation.Token);

        Assert.Equal(0, await PurgeAsync());
        await blockerTransaction.CommitAsync(cancellation.Token);
        var confirmed = await confirmationTask;

        Assert.Equal(JobPostingParseDraftStatus.Confirmed, confirmed.Status);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Confirmed, persisted.Status);
        Assert.Equal(Now, persisted.ConfirmedAt);
    }

    [Fact]
    public async Task PurgeBatchAsync_When_delete_wins_concurrent_provenance_insert_fails_without_orphan()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seeded = await SeedDraftAsync(
            JobPostingParseDraftStatus.Confirmed,
            Now.AddDays(-90),
            withCandidate: true);
        const long advisoryKey = 1_410_107;

        await using var coordinator = new NpgsqlConnection(Fixture.ConnectionString);
        await coordinator.OpenAsync(cancellation.Token);
        await InstallDeleteBarrierAsync(coordinator, advisoryKey, cancellation.Token);
        try
        {
            await using (var lockCommand = coordinator.CreateCommand())
            {
                lockCommand.CommandText = "SELECT pg_advisory_lock(@key);";
                lockCommand.Parameters.AddWithValue("key", advisoryKey);
                await lockCommand.ExecuteScalarAsync(cancellation.Token);
            }

            await using var purgeContext = Fixture.DbContextFactory.CreateDbContext();
            var purgeTask = CreateService(purgeContext)
                .PurgeBatchAsync(cancellation.Token);
            await WaitForBlockedQueryAsync(
                "resumatch_parse_draft_purge",
                cancellation.Token);

            await using var insertContext = Fixture.DbContextFactory.CreateDbContext();
            insertContext.JobPostingSkills.Add(CreateRequirement(
                seeded,
                JobPostingSkillProvenanceStatus.AiActive));
            var insertTask = insertContext.SaveChangesAsync(cancellation.Token);
            await WaitForBlockedQueryAsync(
                "INSERT INTO \"JobPostingSkills\"",
                cancellation.Token);

            await using (var unlockCommand = coordinator.CreateCommand())
            {
                unlockCommand.CommandText = "SELECT pg_advisory_unlock(@key);";
                unlockCommand.Parameters.AddWithValue("key", advisoryKey);
                Assert.True((bool)(await unlockCommand.ExecuteScalarAsync(
                    cancellation.Token))!);
            }

            Assert.Equal(1, await purgeTask);
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => insertTask);
            Assert.Contains(
                FindPostgresException(exception)?.SqlState,
                new[]
                {
                    PostgresErrorCodes.ForeignKeyViolation,
                    PostgresErrorCodes.DeadlockDetected
                });

            await using var verification = Fixture.DbContextFactory.CreateDbContext();
            Assert.False(await verification.JobPostingParseDrafts.AnyAsync(
                item => item.Id == seeded.DraftId,
                cancellation.Token));
            Assert.False(await verification.JobPostingSkills.AnyAsync(
                item => item.SourceParseDraftId == seeded.DraftId,
                cancellation.Token));
        }
        finally
        {
            await RemoveDeleteBarrierAsync(coordinator, CancellationToken.None);
        }
    }

    private async Task<SeededDraft> SeedDraftAsync(
        JobPostingParseDraftStatus status,
        DateTimeOffset lifecycleAt,
        bool withCandidate = false,
        bool withEvidence = false)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = new User
        {
            FirstName = "Retention",
            LastName = "Owner",
            Email = $"retention-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            Description = "C# is required.",
            Status = JobPostingStatus.Draft
        };
        var skill = new Skill
        {
            Name = $"C# {Guid.NewGuid():N}",
            Category = SkillCategory.ProgrammingLanguage
        };
        skill.NormalizedName = skill.Name.ToLowerInvariant();
        context.AddRange(user, posting, skill);
        await context.SaveChangesAsync();

        var draft = CreateDraft(posting, status, lifecycleAt);
        JobPostingParseDraftSkill? candidate = null;
        if (withCandidate)
        {
            candidate = new JobPostingParseDraftSkill
            {
                CandidateName = "C#",
                CanonicalKey = "c#",
                ResolvedSkillId = skill.Id,
                RequirementType = JobSkillRequirementType.Required,
                ProposedImportanceScore = 90,
                Confidence = 0.95m,
                SortOrder = 0,
                ResolutionStatus = SkillResolutionStatus.Exact
            };
            if (withEvidence)
            {
                candidate.Evidence.Add(new JobPostingParseDraftSkillEvidence
                {
                    Text = "C#",
                    Start = 0,
                    Length = 2
                });
            }
            draft.Skills.Add(candidate);
        }

        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync();
        return new SeededDraft(
            user.Id,
            posting.Id,
            skill.Id,
            draft.Id,
            candidate?.Id);
    }

    private static JobPostingParseDraft CreateDraft(
        JobPosting posting,
        JobPostingParseDraftStatus status,
        DateTimeOffset lifecycleAt)
    {
        var draft = new JobPostingParseDraft
        {
            JobPostingId = posting.Id,
            UserId = posting.UserId,
            SourceJobPostingVersion = posting.Version,
            SourceTitle = posting.Title,
            SourceDescription = posting.Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(posting.Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                posting.Title, posting.Description),
            ParserVersion = "job-parser-v1",
            Status = status,
            RequestedAt = lifecycleAt.AddHours(-1),
            UpdatedAt = lifecycleAt,
            ExpiresAt = lifecycleAt.AddHours(1)
        };

        switch (status)
        {
            case JobPostingParseDraftStatus.Processing:
                draft.LeaseOwner = "retention-test-worker";
                draft.LastAttemptAt = lifecycleAt;
                draft.LeaseExpiresAt = lifecycleAt.AddMinutes(5);
                break;
            case JobPostingParseDraftStatus.Ready:
                draft.CompletedAt = lifecycleAt;
                break;
            case JobPostingParseDraftStatus.Failed:
            case JobPostingParseDraftStatus.Superseded:
                draft.CompletedAt = lifecycleAt;
                break;
            case JobPostingParseDraftStatus.Confirmed:
                draft.CompletedAt = lifecycleAt.AddMinutes(-1);
                draft.ConfirmedAt = lifecycleAt;
                break;
        }

        return draft;
    }

    private async Task SeedRequirementAsync(
        SeededDraft seeded,
        JobPostingSkillProvenanceStatus status)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        context.JobPostingSkills.Add(CreateRequirement(seeded, status));
        await context.SaveChangesAsync();
    }

    private static JobPostingSkill CreateRequirement(
        SeededDraft seeded,
        JobPostingSkillProvenanceStatus status) => new()
        {
            JobPostingId = seeded.JobPostingId,
            SkillId = seeded.SkillId,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 90,
            IsAiDetected = true,
            ProvenanceStatus = status,
            SourceParseDraftId = seeded.DraftId,
            SourceParseDraftSkillId = seeded.CandidateId,
            InvalidatedAt = status == JobPostingSkillProvenanceStatus.AiStale
                ? Now.AddDays(-80)
                : null
        };

    private async Task<int> PurgeAsync(int batchSize = 100)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        return await CreateService(context, batchSize)
            .PurgeBatchAsync(CancellationToken.None);
    }

    private static JobPostingParseDraftPurgeService CreateService(
        AppDbContext context,
        int batchSize = 100) => new(
            context,
            Options.Create(new JobPostingParseDraftOptions
            {
                RetentionPeriod = TimeSpan.FromDays(30),
                PurgeBatchSize = batchSize
            }),
            new FixedTimeProvider(Now));

    private static JobPostingParseDraftService CreateDraftService(
        AppDbContext context,
        Guid userId) => new(
            context,
            new TestCurrentUserService(userId),
            new TestParserRegistry(),
            new JobDescriptionPreprocessor(),
            Options.Create(new JobPostingParseDraftOptions()),
            new FixedTimeProvider(Now));

    private async Task WaitForBlockedQueryAsync(
        string queryFragment,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS
                (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND pid <> pg_backend_pid()
                      AND state = 'active'
                      AND wait_event_type = 'Lock'
                      AND query LIKE @pattern
                );
                """;
            command.Parameters.AddWithValue("pattern", $"%{queryFragment}%");
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static async Task InstallDeleteBarrierAsync(
        NpgsqlConnection connection,
        long advisoryKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE OR REPLACE FUNCTION test_pause_parse_draft_delete()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                PERFORM pg_advisory_xact_lock(TG_ARGV[0]::bigint);
                RETURN OLD;
            END;
            $function$;

            CREATE TRIGGER test_pause_parse_draft_delete_trigger
            BEFORE DELETE ON "JobPostingParseDrafts"
            FOR EACH ROW
            EXECUTE FUNCTION test_pause_parse_draft_delete();
            """;
        command.CommandText = command.CommandText.Replace(
            "EXECUTE FUNCTION test_pause_parse_draft_delete();",
            $"EXECUTE FUNCTION test_pause_parse_draft_delete('{advisoryKey}');",
            StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RemoveDeleteBarrierAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DROP TRIGGER IF EXISTS test_pause_parse_draft_delete_trigger
                ON "JobPostingParseDrafts";
            DROP FUNCTION IF EXISTS test_pause_parse_draft_delete();
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private async Task AssertDraftExistsAsync(Guid draftId, bool expected)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(expected, await context.JobPostingParseDrafts
            .AsNoTracking()
            .AnyAsync(item => item.Id == draftId));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => userId;
    }

    private sealed class TestParserRegistry : IJobParserRegistry
    {
        private static readonly IJobDescriptionParser Parser = new TestParser();
        public IJobDescriptionParser Current => Parser;
        public IJobDescriptionParser GetRequired(string parserVersion) =>
            parserVersion == Parser.ParserVersion
                ? Parser
                : throw new KeyNotFoundException(parserVersion);
    }

    private sealed class TestParser : IJobDescriptionParser
    {
        public string ParserVersion => "job-parser-v1";

        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record SeededDraft(
        Guid UserId,
        Guid JobPostingId,
        Guid SkillId,
        Guid DraftId,
        Guid? CandidateId);
}
