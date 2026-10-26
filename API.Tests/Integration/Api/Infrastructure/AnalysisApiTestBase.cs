using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api.Infrastructure;

public abstract class AnalysisApiTestBase : ApiIntegrationTestBase
{
    protected static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    protected AnalysisApiTestBase(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected AuthenticatedHttpClientFactory AuthenticatedClients =>
        new(Application);

    protected AppDbContext CreateDbContext()
    {
        return Fixture.DbContextFactory.CreateDbContext();
    }

    protected static CancellationTokenSource CreateCancellation()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(45));
    }

    protected static User CreateUser(
        int id,
        string email,
        bool isActive = true)
    {
        return new User
        {
            Id = Id(id),
            FirstName = "API",
            LastName = $"User {id}",
            Email = email,
            PasswordHash = "integration-test-password-hash",
            IsActive = isActive,
            CreatedAt = SeedTime
        };
    }

    protected static Resume CreateResume(
        int id,
        Guid userId,
        string title = "Integration Resume")
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

    protected static JobPosting CreateJobPosting(
        int id,
        Guid userId,
        string title = "Integration Job")
    {
        return new JobPosting
        {
            Id = Id(id),
            UserId = userId,
            Title = title,
            CompanyName = "Integration Company",
            Description = "Deterministic integration test job description.",
            Status = JobPostingStatus.Analyzed,
            CreatedAt = SeedTime
        };
    }

    protected static Skill CreateSkill(
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

    protected static ResumeAnalysis CreateAnalysis(
        int id,
        Guid userId,
        Guid resumeId,
        DateTimeOffset analyzedAt,
        Guid? jobPostingId = null)
    {
        return new ResumeAnalysis
        {
            Id = Id(id),
            UserId = userId,
            ResumeId = resumeId,
            JobPostingId = jobPostingId,
            JobPostingUserId = jobPostingId.HasValue ? userId : null,
            Type = jobPostingId.HasValue
                ? AnalysisType.JobMatch
                : AnalysisType.AtsOnly,
            AtsScore = 50m,
            JobMatchScore = jobPostingId.HasValue ? 50m : null,
            StructureScore = 50m,
            FormattingScore = 50m,
            ContentScore = 50m,
            KeywordScore = 50m,
            AnalyzedAt = analyzedAt,
            AnalyzerVersion = "ats-v1",
            CreatedAt = analyzedAt
        };
    }

    protected static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
