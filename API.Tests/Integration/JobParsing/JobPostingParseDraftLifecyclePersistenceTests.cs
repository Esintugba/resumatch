using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftLifecyclePersistenceTests :
    IntegrationTestBase
{
    private const string ActiveFingerprintIndex =
        "UX_JobPostingParseDrafts_ActiveFingerprint";
    private const string SourceInputHashConstraint =
        "CK_JobPostingParseDrafts_SourceInputHash";
    private const string SkillCatalogHashConstraint =
        "CK_JobPostingParseDrafts_SkillCatalogHash";
    private const string SourceTitleConstraint =
        "CK_JobPostingParseDrafts_SourceTitle_NotBlank";
    private const string LeaseOwnerConstraint =
        "CK_JobPostingParseDrafts_LeaseOwner_NotBlank";
    private const string StatusConstraint =
        "CK_JobPostingParseDrafts_Status";
    private const string ProcessingLeaseConstraint =
        "CK_JobPostingParseDrafts_ProcessingLease";
    private const string NextAttemptConstraint =
        "CK_JobPostingParseDrafts_NextAttemptStatus";
    private const string SourceTitle = "Backend Engineer";
    private const string SourceDescription =
        "Strong experience with C# and ASP.NET Core is required.";

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public JobPostingParseDraftLifecyclePersistenceTests(
        PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SaveChanges_SourceSnapshot_PersistsExactInputHashAndNullableCatalogHash()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var posting = await SeedPostingAsync(dbContext, cancellation.Token);
        var draft = CreateDraft(posting);
        dbContext.JobPostingParseDrafts.Add(draft);

        await dbContext.SaveChangesAsync(cancellation.Token);
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == draft.Id, cancellation.Token);

        Assert.Equal(SourceTitle, persisted.SourceTitle);
        Assert.Equal(
            SourceInputHashCalculator.Compute(SourceTitle, SourceDescription),
            persisted.SourceInputHash);
        Assert.Equal(
            DescriptionHashCalculator.Compute(SourceDescription),
            persisted.SourceDescriptionHash);
        Assert.Null(persisted.SkillCatalogHash);
        Assert.NotEqual(0u, persisted.Version);
    }

    public static TheoryData<InvalidLifecycleCase, string> InvalidCases => new()
    {
        { InvalidLifecycleCase.SourceInputHash, SourceInputHashConstraint },
        { InvalidLifecycleCase.SkillCatalogHash, SkillCatalogHashConstraint },
        { InvalidLifecycleCase.SourceTitle, SourceTitleConstraint },
        { InvalidLifecycleCase.LeaseOwner, LeaseOwnerConstraint },
        { InvalidLifecycleCase.Status, StatusConstraint },
        { InvalidLifecycleCase.MissingProcessingLease, ProcessingLeaseConstraint },
        { InvalidLifecycleCase.InvalidLeaseRange, ProcessingLeaseConstraint },
        { InvalidLifecycleCase.NonProcessingLease, ProcessingLeaseConstraint },
        { InvalidLifecycleCase.NextAttemptOutsidePending, NextAttemptConstraint }
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task SaveChanges_InvalidLifecycleValue_EnforcesNamedConstraint(
        InvalidLifecycleCase invalidCase,
        string expectedConstraint)
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var posting = await SeedPostingAsync(dbContext, cancellation.Token);
        var draft = CreateDraft(posting);
        ApplyInvalidCase(draft, invalidCase);
        dbContext.JobPostingParseDrafts.Add(draft);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertPostgres(
            exception,
            PostgresErrorCodes.CheckViolation,
            expectedConstraint);
    }

    [Fact]
    public async Task SaveChanges_SameActiveFingerprintTwice_ReturnsUniqueViolation()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var posting = await SeedPostingAsync(dbContext, cancellation.Token);
        var first = CreateDraft(posting);
        dbContext.JobPostingParseDrafts.Add(first);
        await dbContext.SaveChangesAsync(cancellation.Token);

        dbContext.JobPostingParseDrafts.Add(CreateDraft(posting));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertPostgres(
            exception,
            PostgresErrorCodes.UniqueViolation,
            ActiveFingerprintIndex);
    }

    [Fact]
    public async Task SaveChanges_DifferentFingerprintForSamePosting_IsAllowed()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var posting = await SeedPostingAsync(dbContext, cancellation.Token);
        var first = CreateDraft(posting);
        var second = CreateDraft(posting, title: "Platform Engineer");
        dbContext.JobPostingParseDrafts.AddRange(first, second);

        await dbContext.SaveChangesAsync(cancellation.Token);

        Assert.Equal(2, await dbContext.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_SameFingerprintForDifferentPosting_IsAllowed()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var firstPosting = CreatePosting(user.Id);
        var secondPosting = CreatePosting(user.Id);
        dbContext.AddRange(user, firstPosting, secondPosting);
        await dbContext.SaveChangesAsync(cancellation.Token);
        dbContext.JobPostingParseDrafts.AddRange(
            CreateDraft(firstPosting),
            CreateDraft(secondPosting));

        await dbContext.SaveChangesAsync(cancellation.Token);

        Assert.Equal(2, await dbContext.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task SaveChanges_TerminalFingerprint_DoesNotBlockNewActiveDraft(
        JobPostingParseDraftStatus terminalStatus)
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var posting = await SeedPostingAsync(dbContext, cancellation.Token);
        var terminal = CreateDraft(posting);
        terminal.Status = terminalStatus;
        dbContext.JobPostingParseDrafts.Add(terminal);
        await dbContext.SaveChangesAsync(cancellation.Token);

        dbContext.JobPostingParseDrafts.Add(CreateDraft(posting));
        await dbContext.SaveChangesAsync(cancellation.Token);

        Assert.Equal(2, await dbContext.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_ConcurrentActiveFingerprint_AllowsExactlyOneInsert()
    {
        using var cancellation = CreateCancellation();
        JobPosting posting;
        await using (var seedContext = Fixture.DbContextFactory.CreateDbContext())
        {
            posting = await SeedPostingAsync(seedContext, cancellation.Token);
        }

        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        firstContext.JobPostingParseDrafts.Add(CreateDraft(posting));
        secondContext.JobPostingParseDrafts.Add(CreateDraft(posting));

        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = SaveAfterGateAsync(firstContext, gate.Task, cancellation.Token);
        var secondTask = SaveAfterGateAsync(secondContext, gate.Task, cancellation.Token);
        gate.SetResult();
        var outcomes = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(outcomes, exception => exception is null);
        var failure = Assert.Single(outcomes.OfType<DbUpdateException>());
        AssertPostgres(
            failure,
            PostgresErrorCodes.UniqueViolation,
            ActiveFingerprintIndex);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await verification.JobPostingParseDrafts
            .CountAsync(cancellation.Token));
    }

    private static async Task<Exception?> SaveAfterGateAsync(
        AppDbContext dbContext,
        Task gate,
        CancellationToken cancellationToken)
    {
        await gate;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void ApplyInvalidCase(
        JobPostingParseDraft draft,
        InvalidLifecycleCase invalidCase)
    {
        switch (invalidCase)
        {
            case InvalidLifecycleCase.SourceInputHash:
                draft.SourceInputHash = "invalid";
                break;
            case InvalidLifecycleCase.SkillCatalogHash:
                draft.SkillCatalogHash = "invalid";
                break;
            case InvalidLifecycleCase.SourceTitle:
                draft.SourceTitle = " \t ";
                break;
            case InvalidLifecycleCase.LeaseOwner:
                SetProcessingLease(draft);
                draft.LeaseOwner = " \t ";
                break;
            case InvalidLifecycleCase.Status:
                draft.Status = (JobPostingParseDraftStatus)999;
                break;
            case InvalidLifecycleCase.MissingProcessingLease:
                draft.Status = JobPostingParseDraftStatus.Processing;
                break;
            case InvalidLifecycleCase.InvalidLeaseRange:
                SetProcessingLease(draft);
                draft.LeaseExpiresAt = draft.LastAttemptAt;
                break;
            case InvalidLifecycleCase.NonProcessingLease:
                draft.Status = JobPostingParseDraftStatus.Ready;
                draft.LeaseOwner = "worker-1";
                draft.LastAttemptAt = SeedTime;
                draft.LeaseExpiresAt = SeedTime.AddMinutes(1);
                break;
            case InvalidLifecycleCase.NextAttemptOutsidePending:
                draft.Status = JobPostingParseDraftStatus.Ready;
                draft.NextAttemptAt = SeedTime.AddMinutes(1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }
    }

    private static void SetProcessingLease(JobPostingParseDraft draft)
    {
        draft.Status = JobPostingParseDraftStatus.Processing;
        draft.LeaseOwner = "worker-1";
        draft.LastAttemptAt = SeedTime;
        draft.LeaseExpiresAt = SeedTime.AddMinutes(1);
        draft.NextAttemptAt = null;
    }

    private static async Task<JobPosting> SeedPostingAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        dbContext.AddRange(user, posting);
        await dbContext.SaveChangesAsync(cancellationToken);
        return posting;
    }

    private static User CreateUser() => new()
    {
        FirstName = "Draft",
        LastName = "Owner",
        Email = $"lifecycle-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreatePosting(Guid userId) => new()
    {
        UserId = userId,
        Title = SourceTitle,
        Description = SourceDescription,
        Status = JobPostingStatus.Draft
    };

    private static JobPostingParseDraft CreateDraft(
        JobPosting posting,
        string title = SourceTitle) => new()
    {
        JobPostingId = posting.Id,
        UserId = posting.UserId,
        SourceJobPostingVersion = posting.Version,
        SourceTitle = title,
        SourceDescriptionHash = DescriptionHashCalculator.Compute(
            SourceDescription),
        SourceInputHash = SourceInputHashCalculator.Compute(
            title,
            SourceDescription),
        SourceDescription = SourceDescription,
        ParserVersion = "job-parser-v1",
        Status = JobPostingParseDraftStatus.Pending,
        RequestedAt = SeedTime
    };

    private static void AssertPostgres(
        DbUpdateException exception,
        string expectedSqlState,
        string expectedConstraint)
    {
        var postgres = FindPostgresException(exception);
        Assert.NotNull(postgres);
        Assert.Equal(expectedSqlState, postgres.SqlState);
        Assert.Equal(expectedConstraint, postgres.ConstraintName);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    public enum InvalidLifecycleCase
    {
        SourceInputHash,
        SkillCatalogHash,
        SourceTitle,
        LeaseOwner,
        Status,
        MissingProcessingLease,
        InvalidLeaseRange,
        NonProcessingLease,
        NextAttemptOutsidePending
    }
}
