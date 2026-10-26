using Api.Application.Analysis;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Data;
using Api.DTOs.Analysis;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Tests.Integration.Analysis;

public sealed class AnalysisOwnershipTests : IntegrationTestBase
{
    private const string ResumeOwnershipConstraint =
        "FK_ResumeAnalyses_Resumes_ResumeId_UserId";
    private const string JobPostingOwnershipConstraint =
        "FK_ResumeAnalyses_JobPostings_JobPostingId_JobPostingUserId";

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public AnalysisOwnershipTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AnalyzeAsync_WhenResumeBelongsToAnotherUser_ReturnsNotFoundWithoutPersistence()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeB = CreateResume(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var service = CreateService(dbContext, userA.Id);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resumeB.Id },
                cancellation.Token));

        Assert.Equal("Resume was not found.", exception.Message);
        Assert.False(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenJobPostingBelongsToAnotherUser_ReturnsNotFoundWithoutPersistence()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeA = CreateResume(10, userA.Id);
        var jobPostingB = CreateJobPosting(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeA, jobPostingB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var service = CreateService(dbContext, userA.Id);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resumeA.Id,
                    JobPostingId = jobPostingB.Id
                },
                cancellation.Token));

        Assert.Equal("Job posting was not found.", exception.Message);
        Assert.False(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task GetByIdAsync_WhenAnalysisBelongsToAnotherUser_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeB = CreateResume(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var analysisB = await CreateService(dbContext, userB.Id)
            .AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resumeB.Id },
                cancellation.Token);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService(dbContext, userA.Id).GetByIdAsync(
                analysisB.Id,
                cancellation.Token));

        Assert.Equal("Resume analysis was not found.", exception.Message);
        Assert.True(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(
                analysis =>
                    analysis.Id == analysisB.Id &&
                    analysis.UserId == userB.Id,
                cancellation.Token));
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyOwnedResumeHistoryAndRejectsForeignResume()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeA = CreateResume(10, userA.Id);
        var resumeB = CreateResume(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeA, resumeB);
        await dbContext.SaveChangesAsync(cancellation.Token);

        var analysisA = await CreateService(dbContext, userA.Id)
            .AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resumeA.Id },
                cancellation.Token);
        var analysisB = await CreateService(dbContext, userB.Id)
            .AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resumeB.Id },
                cancellation.Token);
        dbContext.ChangeTracker.Clear();
        var serviceA = CreateService(dbContext, userA.Id);

        var history = await serviceA.GetHistoryAsync(
            resumeA.Id,
            1,
            20,
            cancellation.Token);

        Assert.Equal(1, history.TotalCount);
        Assert.Equal(analysisA.Id, Assert.Single(history.Items).Id);
        Assert.DoesNotContain(
            history.Items,
            item => item.Id == analysisB.Id);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            serviceA.GetHistoryAsync(
                resumeB.Id,
                1,
                20,
                cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_WhenResumeOwnershipIsCrossUser_EnforcesCompositeForeignKey()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeB = CreateResume(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        dbContext.ResumeAnalyses.Add(CreateAnalysis(
            100,
            userA.Id,
            resumeB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertForeignKeyViolation(exception, ResumeOwnershipConstraint);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_WhenJobPostingOwnershipIsCrossUser_EnforcesCompositeForeignKey()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeA = CreateResume(10, userA.Id);
        var jobPostingB = CreateJobPosting(20, userB.Id);
        dbContext.AddRange(userA, userB, resumeA, jobPostingB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var invalidAnalysis = CreateAnalysis(
            100,
            userA.Id,
            resumeA.Id);
        invalidAnalysis.Type = AnalysisType.JobMatch;
        invalidAnalysis.JobPostingId = jobPostingB.Id;
        invalidAnalysis.JobPostingUserId = userA.Id;
        dbContext.ResumeAnalyses.Add(invalidAnalysis);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertForeignKeyViolation(exception, JobPostingOwnershipConstraint);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task AnalyzeAsync_ValidJobMatch_PersistsAllCompositeOwnershipValues()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var resumeA = CreateResume(10, userA.Id);
        var jobPostingA = CreateJobPosting(20, userA.Id);
        dbContext.AddRange(userA, resumeA, jobPostingA);
        await dbContext.SaveChangesAsync(cancellation.Token);

        var response = await CreateService(dbContext, userA.Id)
            .AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resumeA.Id,
                    JobPostingId = jobPostingA.Id
                },
                cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.Resume)
            .Include(analysis => analysis.JobPosting)
            .SingleAsync(
                analysis => analysis.Id == response.Id,
                cancellation.Token);

        Assert.Equal(userA.Id, persisted.UserId);
        Assert.Equal(resumeA.Id, persisted.ResumeId);
        Assert.Equal(userA.Id, persisted.Resume.UserId);
        Assert.Equal(jobPostingA.Id, persisted.JobPostingId);
        Assert.Equal(userA.Id, persisted.JobPostingUserId);
        Assert.NotNull(persisted.JobPosting);
        Assert.Equal(userA.Id, persisted.JobPosting!.UserId);
    }

    [Fact]
    public async Task UnknownIdentifiers_ReturnNotFoundWithoutDatabaseErrorsOrPersistence()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var resumeA = CreateResume(10, userA.Id);
        dbContext.AddRange(userA, resumeA);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var service = CreateService(dbContext, userA.Id);
        var unknownResumeId = Id(9_001);
        var unknownJobPostingId = Id(9_002);
        var unknownAnalysisId = Id(9_003);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = unknownResumeId },
                cancellation.Token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resumeA.Id,
                    JobPostingId = unknownJobPostingId
                },
                cancellation.Token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetByIdAsync(
                unknownAnalysisId,
                cancellation.Token));

        Assert.False(await dbContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task ResourceIdSubstitution_DoesNotBypassOwnershipForSimilarContent()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser(1, "user-a@example.com");
        var userB = CreateUser(2, "user-b@example.com");
        var resumeA = CreateResume(10, userA.Id, "Identical Resume");
        var resumeB = CreateResume(11, userB.Id, "Identical Resume");
        var jobPostingA = CreateJobPosting(20, userA.Id, "Identical Job");
        var jobPostingB = CreateJobPosting(21, userB.Id, "Identical Job");
        dbContext.AddRange(
            userA,
            userB,
            resumeA,
            resumeB,
            jobPostingA,
            jobPostingB);
        await dbContext.SaveChangesAsync(cancellation.Token);
        var serviceA = CreateService(dbContext, userA.Id);

        var valid = await serviceA.AnalyzeAsync(
            new AnalyzeResumeRequest
            {
                ResumeId = resumeA.Id,
                JobPostingId = jobPostingA.Id
            },
            cancellation.Token);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            serviceA.AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resumeB.Id,
                    JobPostingId = jobPostingA.Id
                },
                cancellation.Token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            serviceA.AnalyzeAsync(
                new AnalyzeResumeRequest
                {
                    ResumeId = resumeA.Id,
                    JobPostingId = jobPostingB.Id
                },
                cancellation.Token));

        var persisted = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .ToListAsync(cancellation.Token);
        var onlyAnalysis = Assert.Single(persisted);
        Assert.Equal(valid.Id, onlyAnalysis.Id);
        Assert.Equal(userA.Id, onlyAnalysis.UserId);
        Assert.Equal(resumeA.Id, onlyAnalysis.ResumeId);
        Assert.Equal(jobPostingA.Id, onlyAnalysis.JobPostingId);
    }

    private static ResumeAnalysisService CreateService(
        AppDbContext dbContext,
        Guid userId)
    {
        return new ResumeAnalysisService(
            dbContext,
            new TestCurrentUserService(userId),
            new AnalysisInputLoader(dbContext),
            new AnalyzerRegistry([new AnalysisEngineV1()]));
    }

    private static User CreateUser(int id, string email)
    {
        return new User
        {
            Id = Id(id),
            FirstName = "Integration",
            LastName = $"User {id}",
            Email = email,
            PasswordHash = "not-a-real-password-hash",
            IsActive = true,
            CreatedAt = SeedTime
        };
    }

    private static Resume CreateResume(
        int id,
        Guid userId,
        string title = "Ownership Resume")
    {
        return new Resume
        {
            Id = Id(id),
            UserId = userId,
            Title = title,
            ProfessionalTitle = "Software Engineer",
            IsMasterResume = true,
            CreatedAt = SeedTime
        };
    }

    private static JobPosting CreateJobPosting(
        int id,
        Guid userId,
        string title = "Ownership Job")
    {
        return new JobPosting
        {
            Id = Id(id),
            UserId = userId,
            Title = title,
            Description = "Identical deterministic job description.",
            Status = JobPostingStatus.Analyzed,
            CreatedAt = SeedTime
        };
    }

    private static ResumeAnalysis CreateAnalysis(
        int id,
        Guid userId,
        Guid resumeId)
    {
        return new ResumeAnalysis
        {
            Id = Id(id),
            UserId = userId,
            ResumeId = resumeId,
            Type = AnalysisType.AtsOnly,
            AtsScore = 50m,
            StructureScore = 50m,
            FormattingScore = 50m,
            ContentScore = 50m,
            KeywordScore = 50m,
            AnalyzedAt = SeedTime,
            AnalyzerVersion = AnalysisEngineV1.Version,
            CreatedAt = SeedTime
        };
    }

    private static void AssertForeignKeyViolation(
        DbUpdateException exception,
        string expectedConstraint)
    {
        var postgresException = Assert.IsType<PostgresException>(
            exception.InnerException);
        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            postgresException.SqlState);
        Assert.Equal(expectedConstraint, postgresException.ConstraintName);
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
}
