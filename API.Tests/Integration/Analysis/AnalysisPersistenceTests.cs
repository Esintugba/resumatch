using System.Collections.Immutable;
using System.Data.Common;
using Api.Application.Analysis;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobPostings;
using Api.Data;
using Api.DTOs.Analysis;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace API.Tests.Integration.Analysis;

public sealed class AnalysisPersistenceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    public AnalysisPersistenceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AnalyzeAsync_AtsOnly_PersistsOwnedScoresAndVersion()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var service = CreateService(dbContext, user.Id);

        var response = await service.AnalyzeAsync(
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(
                analysis => analysis.Id == response.Id,
                cancellation.Token);

        Assert.Equal(resume.Id, persisted.ResumeId);
        Assert.Equal(user.Id, persisted.UserId);
        Assert.Null(persisted.JobPostingId);
        Assert.Null(persisted.JobPostingUserId);
        Assert.Null(persisted.SourceJobPostingVersion);
        Assert.Null(persisted.SourceJobPostingInputHash);
        Assert.Null(persisted.SourceJobPostingSkillSetHash);
        Assert.Equal(AnalysisType.AtsOnly, persisted.Type);
        Assert.Null(persisted.JobMatchScore);
        Assert.Equal(AnalysisEngineV1.Version, persisted.AnalyzerVersion);
        Assert.NotEqual(default, persisted.AnalyzedAt);
        AssertScore(persisted.AtsScore);
        AssertScore(persisted.StructureScore);
        AssertScore(persisted.FormattingScore);
        AssertScore(persisted.ContentScore);
        AssertScore(persisted.KeywordScore);
    }

    [Fact]
    public async Task AnalyzeAsync_WithIncompleteResume_PersistsCompleteIssueAggregate()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var service = CreateService(dbContext, user.Id);

        var response = await service.AnalyzeAsync(
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(analysis => analysis.Issues)
            .Include(analysis => analysis.Recommendations)
            .SingleAsync(
                analysis => analysis.Id == response.Id,
                cancellation.Token);

        Assert.NotEmpty(persisted.Issues);
        Assert.NotEmpty(persisted.Recommendations);
        Assert.All(persisted.Issues, issue =>
        {
            Assert.Equal(persisted.Id, issue.ResumeAnalysisId);
            Assert.False(string.IsNullOrWhiteSpace(issue.Code));
        });
        Assert.All(persisted.Recommendations, recommendation =>
        {
            Assert.Equal(persisted.Id, recommendation.ResumeAnalysisId);
            Assert.Equal(RecommendationStatus.Pending, recommendation.Status);
            Assert.False(recommendation.IsAiGenerated);
        });
    }

    [Fact]
    public async Task AnalyzeAsync_JobMatch_PersistsMatchedAndMissingKeywords()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var matchedSkill = CreateSkill(100, "C#", "c#");
        var missingSkill = CreateSkill(101, "Docker", "docker");
        var jobPosting = CreateJobPosting(user.Id, 200);

        dbContext.Skills.AddRange(matchedSkill, missingSkill);
        dbContext.ResumeSkills.Add(new ResumeSkill
        {
            Id = Id(102),
            ResumeId = resume.Id,
            SkillId = matchedSkill.Id,
            SortOrder = 0
        });
        dbContext.JobPostings.Add(jobPosting);
        dbContext.JobPostingSkills.AddRange(
            new JobPostingSkill
            {
                Id = Id(103),
                JobPostingId = jobPosting.Id,
                SkillId = matchedSkill.Id,
                RequirementType = JobSkillRequirementType.Required,
                ImportanceScore = 100
            },
            new JobPostingSkill
            {
                Id = Id(104),
                JobPostingId = jobPosting.Id,
                SkillId = missingSkill.Id,
                RequirementType = JobSkillRequirementType.Preferred,
                ImportanceScore = 100
            });
        await dbContext.SaveChangesAsync(cancellation.Token);

        var service = CreateService(dbContext, user.Id);
        var response = await service.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = jobPosting.Id
            },
            cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.KeywordMatches)
            .SingleAsync(
                analysis => analysis.Id == response.Id,
                cancellation.Token);

        Assert.Equal(AnalysisType.JobMatch, persisted.Type);
        Assert.Equal(jobPosting.Id, persisted.JobPostingId);
        Assert.Equal(jobPosting.Version, persisted.SourceJobPostingVersion);
        Assert.Equal(
            SourceInputHashCalculator.Compute(
                jobPosting.Title,
                jobPosting.Description),
            persisted.SourceJobPostingInputHash);
        Assert.Equal(
            JobPostingSkillSnapshotHashCalculator.Compute(
            [
                new JobPostingSkillFingerprintItem(
                    matchedSkill.Id,
                    JobSkillRequirementType.Required,
                    100,
                    matchedSkill.Name,
                    matchedSkill.NormalizedName),
                new JobPostingSkillFingerprintItem(
                    missingSkill.Id,
                    JobSkillRequirementType.Preferred,
                    100,
                    missingSkill.Name,
                    missingSkill.NormalizedName)
            ]),
            persisted.SourceJobPostingSkillSetHash);
        Assert.NotNull(persisted.JobMatchScore);
        Assert.Equal(AnalysisEngineV1.Version, persisted.AnalyzerVersion);
        Assert.Equal(2, persisted.KeywordMatches.Count);
        Assert.Contains(
            persisted.KeywordMatches,
            match =>
                match.SkillId == matchedSkill.Id &&
                match.Status == KeywordMatchStatus.Matched);
        Assert.Contains(
            persisted.KeywordMatches,
            match =>
                match.SkillId == missingSkill.Id &&
                match.Status == KeywordMatchStatus.Missing);
        Assert.Equal(
            100.00m,
            persisted.KeywordMatches.Sum(match => match.Weight));
        Assert.Equal(
            persisted.KeywordMatches
                .Where(match => match.Status == KeywordMatchStatus.Matched)
                .Sum(match => match.Weight),
            persisted.JobMatchScore);
    }

    [Fact]
    public async Task AnalyzeAsync_JobWithoutRequirements_PersistsNullScoreAndInputIssue()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var jobPosting = CreateJobPosting(user.Id, 300);
        dbContext.JobPostings.Add(jobPosting);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var service = CreateService(dbContext, user.Id);

        var response = await service.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = jobPosting.Id
            },
            cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.Issues)
            .SingleAsync(
                analysis => analysis.Id == response.Id,
                cancellation.Token);

        Assert.Equal(AnalysisType.JobMatch, persisted.Type);
        Assert.Null(persisted.JobMatchScore);
        Assert.Contains(
            persisted.Issues,
            issue => issue.Code == "JOB.INP.001");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenChildConstraintFails_RollsBackWholeAggregate()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var jobPosting = CreateJobPosting(user.Id, 400);
        dbContext.JobPostings.Add(jobPosting);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var service = CreateService(
            dbContext,
            user.Id,
            new ConstraintViolatingAnalyzer());

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resume.Id,
                    JobPostingId = jobPosting.Id
                },
                cancellation.Token));

        await using var verificationContext =
            Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await verificationContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
        Assert.False(await verificationContext.AnalysisIssues
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
        Assert.False(await verificationContext.KeywordMatches
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
        Assert.False(await verificationContext.AnalysisRecommendations
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_WhenChildChangesAfterRootRead_UsesOneRepeatableSnapshot()
    {
        using var cancellation = CreateCancellation();
        Guid userId;
        Guid resumeId;
        Guid experienceId;

        await using (var seedContext =
                     Fixture.DbContextFactory.CreateDbContext())
        {
            var seeded = await SeedUserAndResumeAsync(
                seedContext,
                cancellation.Token);
            userId = seeded.User.Id;
            resumeId = seeded.Resume.Id;
            experienceId = Id(501);
            seedContext.ResumeExperiences.Add(new ResumeExperience
            {
                Id = experienceId,
                ResumeId = resumeId,
                CompanyName = "Snapshot Company",
                Position = "Engineer",
                StartDate = new DateOnly(2024, 1, 1),
                Description = "before concurrent update",
                SortOrder = 0
            });
            await seedContext.SaveChangesAsync(cancellation.Token);
        }

        var barrier = new ResumeRootReadBarrierInterceptor();
        await using var loaderContext =
            Fixture.DbContextFactory.CreateDbContext(barrier);
        var loader = new AnalysisInputLoader(loaderContext);
        var loadTask = loader.LoadAsync(
            resumeId,
            null,
            userId,
            SeedTime,
            cancellation.Token);

        try
        {
            await barrier.WaitUntilRootReadAsync(cancellation.Token);

            await using var writerContext =
                Fixture.DbContextFactory.CreateDbContext();
            var experience = await writerContext.ResumeExperiences
                .SingleAsync(
                    item => item.Id == experienceId,
                    cancellation.Token);
            experience.Description = "after concurrent update";
            await writerContext.SaveChangesAsync(cancellation.Token);
        }
        finally
        {
            barrier.Release();
        }

        var input = await loadTask;
        Assert.Equal(
            "before concurrent update",
            Assert.Single(input.Resume.Experiences).Description);

        await using var verificationContext =
            Fixture.DbContextFactory.CreateDbContext();
        var currentDescription = await verificationContext.ResumeExperiences
            .AsNoTracking()
            .Where(item => item.Id == experienceId)
            .Select(item => item.Description)
            .SingleAsync(cancellation.Token);
        Assert.Equal("after concurrent update", currentDescription);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceChangesAfterSnapshot_RejectsWithoutAggregate()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var posting = CreateJobPosting(user.Id, 600);
        dbContext.JobPostings.Add(posting);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var blockingAnalyzer = new BlockingAnalysisEngine();
        var service = CreateService(dbContext, user.Id, blockingAnalyzer);

        var analysisTask = Task.Run(() => service.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = posting.Id
            },
            cancellation.Token));

        await blockingAnalyzer.WaitUntilEnteredAsync(cancellation.Token);
        try
        {
            await using var writer = Fixture.DbContextFactory.CreateDbContext();
            var current = await writer.JobPostings.SingleAsync(
                item => item.Id == posting.Id,
                cancellation.Token);
            current.Description += " Source changed.";
            await writer.SaveChangesAsync(cancellation.Token);
        }
        finally
        {
            blockingAnalyzer.Release();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => analysisTask);
        await AssertNoAnalysisAggregateAsync(cancellation.Token);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenEligibleSkillChangesAfterSnapshot_RejectsWithoutAggregate()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var posting = CreateJobPosting(user.Id, 700);
        var skill = CreateSkill(701, "PostgreSQL", "postgresql");
        var requirement = new JobPostingSkill
        {
            Id = Id(702),
            JobPostingId = posting.Id,
            SkillId = skill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 80
        };
        dbContext.AddRange(posting, skill, requirement);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var blockingAnalyzer = new BlockingAnalysisEngine();
        var service = CreateService(dbContext, user.Id, blockingAnalyzer);

        var analysisTask = Task.Run(() => service.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = posting.Id
            },
            cancellation.Token));

        await blockingAnalyzer.WaitUntilEnteredAsync(cancellation.Token);
        try
        {
            await using var writer = Fixture.DbContextFactory.CreateDbContext();
            var current = await writer.JobPostingSkills.SingleAsync(
                item => item.Id == requirement.Id,
                cancellation.Token);
            current.ImportanceScore = 81;
            await writer.SaveChangesAsync(cancellation.Token);
        }
        finally
        {
            blockingAnalyzer.Release();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => analysisTask);
        await AssertNoAnalysisAggregateAsync(cancellation.Token);
    }

    [Fact]
    public async Task AnalyzeAsync_MetadataOnlyUpdate_AllowsSemanticSnapshotAndKeepsOriginalVersion()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedUserAndResumeAsync(
            dbContext,
            cancellation.Token);
        var posting = CreateJobPosting(user.Id, 800);
        dbContext.JobPostings.Add(posting);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var snapshotVersion = posting.Version;
        var blockingAnalyzer = new BlockingAnalysisEngine();
        var service = CreateService(dbContext, user.Id, blockingAnalyzer);

        var analysisTask = Task.Run(() => service.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = posting.Id
            },
            cancellation.Token));

        await blockingAnalyzer.WaitUntilEnteredAsync(cancellation.Token);
        try
        {
            await using var writer = Fixture.DbContextFactory.CreateDbContext();
            var current = await writer.JobPostings.SingleAsync(
                item => item.Id == posting.Id,
                cancellation.Token);
            current.CompanyName = "Metadata-only company update";
            await writer.SaveChangesAsync(cancellation.Token);
        }
        finally
        {
            blockingAnalyzer.Release();
        }

        var response = await analysisTask;
        Assert.Equal(snapshotVersion, response.SourceJobPostingVersion);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var currentVersion = await verification.JobPostings
            .AsNoTracking()
            .Where(item => item.Id == posting.Id)
            .Select(item => item.Version)
            .SingleAsync(cancellation.Token);
        var persisted = await verification.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(item => item.Id == response.Id,
                cancellation.Token);
        Assert.NotEqual(snapshotVersion, currentVersion);
        Assert.Equal(snapshotVersion, persisted.SourceJobPostingVersion);
    }

    [Fact]
    public async Task Finalization_share_lock_serializes_source_update_after_analysis_commit()
    {
        using var cancellation = CreateCancellation();
        User user;
        Resume resume;
        JobPosting posting;
        await using (var seedContext = Fixture.DbContextFactory.CreateDbContext())
        {
            (user, resume) = await SeedUserAndResumeAsync(
                seedContext,
                cancellation.Token);
            posting = CreateJobPosting(user.Id, 900);
            seedContext.JobPostings.Add(posting);
            await seedContext.SaveChangesAsync(cancellation.Token);
        }

        var barrier = new AnalysisFinalizationBarrierInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(barrier)
            .Options;
        await using var analysisContext = new AppDbContext(options);
        var analysisService = CreateService(analysisContext, user.Id);
        var analysisTask = analysisService.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = posting.Id
            },
            cancellation.Token);

        await barrier.WaitUntilSavingAsync(cancellation.Token);
        await using var updateContext = Fixture.DbContextFactory.CreateDbContext();
        var updateService = new JobPostingService(
            updateContext,
            new TestCurrentUserService(user.Id),
            TimeProvider.System);
        var updateTask = updateService.UpdateAsync(
            posting.Id,
            posting.Version,
            new UpdateJobPostingRequest
            {
                Title = posting.Title,
                Description = posting.Description + " changed after finalization"
            },
            cancellation.Token);

        await WaitForBlockedForUpdateAsync(cancellation.Token);
        Assert.False(updateTask.IsCompleted);
        barrier.Release();

        var analysis = await analysisTask;
        await updateTask;

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(item => item.Id == analysis.Id,
                cancellation.Token);
        var currentPosting = await verification.JobPostings
            .AsNoTracking()
            .SingleAsync(item => item.Id == posting.Id,
                cancellation.Token);
        Assert.Equal(posting.Version, persisted.SourceJobPostingVersion);
        Assert.Equal(
            SourceInputHashCalculator.Compute(
                posting.Title,
                posting.Description),
            persisted.SourceJobPostingInputHash);
        Assert.NotEqual(
            persisted.SourceJobPostingInputHash,
            SourceInputHashCalculator.Compute(
                currentPosting.Title,
                currentPosting.Description));
    }

    private async Task WaitForBlockedForUpdateAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND pid <> pg_backend_pid()
                      AND wait_event_type = 'Lock'
                      AND query LIKE '%FOR UPDATE%');
                """;
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private async Task AssertNoAnalysisAggregateAsync(
        CancellationToken cancellationToken)
    {
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Empty(await verification.ResumeAnalyses
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await verification.AnalysisIssues
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await verification.KeywordMatches
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await verification.AnalysisRecommendations
            .AsNoTracking()
            .ToListAsync(cancellationToken));
    }

    private static ResumeAnalysisService CreateService(
        AppDbContext dbContext,
        Guid userId,
        IAnalysisEngine? analyzer = null)
    {
        analyzer ??= new AnalysisEngineV1();
        return new ResumeAnalysisService(
            dbContext,
            new TestCurrentUserService(userId),
            new AnalysisInputLoader(dbContext),
            new AnalyzerRegistry([analyzer]));
    }

    private static async Task<(User User, Resume Resume)>
        SeedUserAndResumeAsync(
            AppDbContext dbContext,
            CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Id(1),
            FirstName = "Integration",
            LastName = "User",
            Email = "integration@example.com",
            PasswordHash = "not-a-real-password-hash",
            IsActive = true,
            CreatedAt = SeedTime
        };
        var resume = new Resume
        {
            Id = Id(2),
            UserId = user.Id,
            Title = "Integration Test Resume",
            ProfessionalTitle = "Software Engineer",
            IsMasterResume = true,
            CreatedAt = SeedTime
        };

        dbContext.Users.Add(user);
        dbContext.Resumes.Add(resume);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (user, resume);
    }

    private static Skill CreateSkill(
        int id,
        string name,
        string normalizedName)
    {
        return new Skill
        {
            Id = Id(id),
            Name = name,
            NormalizedName = normalizedName,
            Category = SkillCategory.Backend,
            CreatedAt = SeedTime
        };
    }

    private static JobPosting CreateJobPosting(Guid userId, int id)
    {
        return new JobPosting
        {
            Id = Id(id),
            UserId = userId,
            Title = "Backend Engineer",
            Description = "Backend engineering role.",
            Status = JobPostingStatus.Analyzed,
            CreatedAt = SeedTime
        };
    }

    private static void AssertScore(decimal score)
    {
        Assert.InRange(score, 0m, 100m);
    }

    private static CancellationTokenSource CreateCancellation()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(45));
    }

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "integration@example.com";
        public bool IsAuthenticated => true;

        public Guid GetRequiredUserId()
        {
            return UserId!.Value;
        }
    }

    private sealed class BlockingAnalysisEngine : IAnalysisEngine
    {
        private readonly AnalysisEngineV1 _inner = new();
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AnalyzerVersion => AnalysisEngineV1.Version;

        public AnalysisResult Analyze(AnalysisInput input)
        {
            _entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            return _inner.Analyze(input);
        }

        public Task WaitUntilEnteredAsync(CancellationToken cancellationToken) =>
            _entered.Task.WaitAsync(cancellationToken);

        public void Release() => _release.TrySetResult();
    }

    private sealed class AnalysisFinalizationBarrierInterceptor :
        SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _saving =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<ResumeAnalysis>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                _saving.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public Task WaitUntilSavingAsync(CancellationToken cancellationToken) =>
            _saving.Task.WaitAsync(cancellationToken);

        public void Release() => _release.TrySetResult();
    }

    private sealed class ConstraintViolatingAnalyzer : IAnalysisEngine
    {
        public string AnalyzerVersion => AnalysisEngineV1.Version;

        public AnalysisResult Analyze(AnalysisInput input)
        {
            return new AnalysisResult(
                50m,
                100m,
                50m,
                50m,
                50m,
                50m,
                AnalyzerVersion,
                ImmutableArray<CategoryScoreResult>.Empty,
                ImmutableArray<RuleResult>.Empty,
                [
                    new AnalysisIssueResult(
                        AnalysisIssueType.Content,
                        IssueSeverity.Warning,
                        new string('X', 101),
                        "Constraint failure",
                        "This issue intentionally exceeds the code limit.",
                        "Content")
                ],
                [
                    new KeywordMatchResult(
                        null,
                        "atomicity",
                        KeywordMatchStatus.Matched,
                        100m,
                        "Skills")
                ],
                [
                    new AnalysisRecommendationResult(
                        "TEST.ATOMIC",
                        RecommendationType.Other,
                        RecommendationPriority.Medium,
                        "Atomic persistence",
                        "This recommendation is part of the failed aggregate.",
                        "Content",
                        null,
                        null,
                        false,
                        RecommendationStatus.Pending)
                ]);
        }
    }

    private sealed class ResumeRootReadBarrierInterceptor
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _rootRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continue = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hasPaused;

        public Task WaitUntilRootReadAsync(CancellationToken cancellationToken)
        {
            return _rootRead.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _continue.TrySetResult();
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM \"Resumes\"",
                    StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _hasPaused, 1, 0) == 0)
            {
                _rootRead.TrySetResult();
                await _continue.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
