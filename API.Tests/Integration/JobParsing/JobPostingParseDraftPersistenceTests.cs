using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Api.Application.JobParsing.Engine.Preprocessing;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftPersistenceTests : IntegrationTestBase
{
    private const string OwnershipConstraint =
        "FK_JobPostingParseDrafts_JobPostings_JobPostingId_UserId";
    private const string SortOrderIndex =
        "IX_JobPostingParseDraftSkills_JobPostingParseDraftId_SortOrder";
    private const string CanonicalKeyIndex =
        "IX_JobPostingParseDraftSkills_JobPostingParseDraftId_Canonical~";
    private const string EvidenceRangeIndex =
        "IX_JobPostingParseDraftSkillEvidences_JobPostingParseDraftSkil~";
    private const string SkillForeignKey =
        "FK_JobPostingParseDraftSkills_Skills_ResolvedSkillId";

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public JobPostingParseDraftPersistenceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SaveChanges_CrossUserJobPostingDraft_EnforcesCompositeOwnershipForeignKey()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var userA = CreateUser("owner-a@example.com");
        var userB = CreateUser("owner-b@example.com");
        var posting = CreateJobPosting(userA.Id);
        dbContext.AddRange(userA, userB, posting);
        await dbContext.SaveChangesAsync(cancellation.Token);

        dbContext.JobPostingParseDrafts.Add(CreateDraft(
            posting.Id,
            userB.Id,
            posting.Version));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertPostgres(
            exception,
            PostgresErrorCodes.ForeignKeyViolation,
            OwnershipConstraint);
    }

    [Fact]
    public async Task SaveChanges_CompleteAggregate_PersistsAllFieldsEvidenceAndXmin()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (_, posting, skill) = await SeedPrincipalsAsync(
            dbContext,
            cancellation.Token);
        var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
        AddCompleteSkills(draft, skill.Id);
        dbContext.JobPostingParseDrafts.Add(draft);

        await dbContext.SaveChangesAsync(cancellation.Token);

        Assert.NotEqual(0u, draft.Version);
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.JobPostingParseDrafts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Skills)
                .ThenInclude(item => item.Evidence)
            .SingleAsync(item => item.Id == draft.Id, cancellation.Token);

        Assert.Equal(posting.UserId, persisted.UserId);
        Assert.Equal(posting.Id, persisted.JobPostingId);
        Assert.Equal(posting.Version, persisted.SourceJobPostingVersion);
        Assert.Equal(Hash('a'), persisted.SourceDescriptionHash);
        Assert.Equal(SourceDescription, persisted.SourceDescription);
        Assert.Equal("job-parser-v1", persisted.ParserVersion);
        Assert.Equal(JobPostingParseDraftStatus.Ready, persisted.Status);
        Assert.Equal(2, persisted.Skills.Count);

        var exact = persisted.Skills.Single(item => item.CanonicalKey == "c#");
        Assert.Equal(JobSkillRequirementType.Required, exact.RequirementType);
        Assert.Equal(100, exact.ProposedImportanceScore);
        Assert.Equal(0.9500m, exact.Confidence);
        Assert.Equal(SkillResolutionStatus.Exact, exact.ResolutionStatus);
        Assert.Equal(0, exact.SortOrder);
        Assert.Equal(2, exact.Evidence.Count);
        Assert.Contains(exact.Evidence, item =>
            item.Text == "C#" && item.Start == 9 && item.Length == 2);

        var unresolved = persisted.Skills.Single(
            item => item.CanonicalKey == "docker");
        Assert.Null(unresolved.ResolvedSkillId);
        Assert.Equal(SkillResolutionStatus.Unresolved, unresolved.ResolutionStatus);
        Assert.Equal(1, unresolved.SortOrder);
        Assert.Equal(2, unresolved.Evidence.Count);
    }

    [Fact]
    public async Task SaveChanges_DuplicateSortOrderInSameDraft_ReturnsUniqueViolation()
    {
        var exception = await SaveDuplicateSkillAsync(
            duplicateSortOrder: true,
            CancellationToken.None);

        AssertPostgres(exception, PostgresErrorCodes.UniqueViolation, SortOrderIndex);
    }

    [Fact]
    public async Task SaveChanges_DuplicateCanonicalKeyInSameDraft_ReturnsUniqueViolation()
    {
        var exception = await SaveDuplicateSkillAsync(
            duplicateSortOrder: false,
            CancellationToken.None);

        AssertPostgres(exception, PostgresErrorCodes.UniqueViolation, CanonicalKeyIndex);
    }

    [Fact]
    public async Task SaveChanges_DuplicateEvidenceRangeInSameSkill_ReturnsUniqueViolation()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (_, posting, skill) = await SeedPrincipalsAsync(
            dbContext,
            cancellation.Token);
        var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
        var draftSkill = CreateExactDraftSkill(skill.Id, 0, "c#");
        draftSkill.Evidence.Add(CreateEvidence("C#", 9));
        draftSkill.Evidence.Add(CreateEvidence("C#", 9));
        draft.Skills.Add(draftSkill);
        dbContext.JobPostingParseDrafts.Add(draft);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertPostgres(
            exception,
            PostgresErrorCodes.UniqueViolation,
            EvidenceRangeIndex);
    }

    [Fact]
    public async Task SaveChanges_SameOrderCanonicalKeyAndEvidenceRangeAcrossDrafts_IsAllowed()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (_, posting, skill) = await SeedPrincipalsAsync(
            dbContext,
            cancellation.Token);
        var first = CreateDraft(posting.Id, posting.UserId, posting.Version);
        var second = CreateDraft(posting.Id, posting.UserId, posting.Version);
        second.SourceTitle = "Platform Engineer";
        second.SourceInputHash = SourceInputHashCalculator.Compute(
            second.SourceTitle,
            second.SourceDescription);
        first.Skills.Add(CreateExactDraftSkill(skill.Id, 0, "c#"));
        second.Skills.Add(CreateExactDraftSkill(skill.Id, 0, "c#"));
        dbContext.JobPostingParseDrafts.AddRange(first, second);

        await dbContext.SaveChangesAsync(cancellation.Token);

        Assert.Equal(2, await dbContext.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
        Assert.Equal(2, await dbContext.JobPostingParseDraftSkills.CountAsync(
            cancellation.Token));
    }

    public static TheoryData<InvalidConstraintCase, string> InvalidConstraintCases =>
        new()
        {
            { InvalidConstraintCase.DescriptionHash, "CK_JobPostingParseDrafts_SourceDescriptionHash" },
            { InvalidConstraintCase.ParserVersion, "CK_JobPostingParseDrafts_ParserVersion_NotBlank" },
            { InvalidConstraintCase.SourceVersion, "CK_JobPostingParseDrafts_SourceJobPostingVersion" },
            { InvalidConstraintCase.AttemptCount, "CK_JobPostingParseDrafts_AttemptCount" },
            { InvalidConstraintCase.SortOrder, "CK_JobPostingParseDraftSkills_SortOrder" },
            { InvalidConstraintCase.ImportanceBelowRange, "CK_JobPostingParseDraftSkills_ProposedImportanceScore" },
            { InvalidConstraintCase.ImportanceAboveRange, "CK_JobPostingParseDraftSkills_ProposedImportanceScore" },
            { InvalidConstraintCase.ConfidenceBelowRange, "CK_JobPostingParseDraftSkills_Confidence" },
            { InvalidConstraintCase.ConfidenceAboveRange, "CK_JobPostingParseDraftSkills_Confidence" },
            { InvalidConstraintCase.EvidenceStart, "CK_JobPostingParseDraftSkillEvidences_Start" },
            { InvalidConstraintCase.EvidenceLength, "CK_JobPostingParseDraftSkillEvidences_Length" },
            { InvalidConstraintCase.ExactWithoutSkill, "CK_JobPostingParseDraftSkills_Resolution" },
            { InvalidConstraintCase.UnresolvedWithSkill, "CK_JobPostingParseDraftSkills_Resolution" }
        };

    [Theory]
    [MemberData(nameof(InvalidConstraintCases))]
    public async Task SaveChanges_InvalidValue_EnforcesNamedCheckConstraint(
        InvalidConstraintCase invalidCase,
        string expectedConstraint)
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (_, posting, skill) = await SeedPrincipalsAsync(
            dbContext,
            cancellation.Token);
        var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
        var draftSkill = CreateExactDraftSkill(skill.Id, 0, "c#");
        draft.Skills.Add(draftSkill);
        ApplyInvalidValue(invalidCase, draft, draftSkill, skill.Id);
        dbContext.JobPostingParseDrafts.Add(draft);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(cancellation.Token));

        AssertPostgres(
            exception,
            PostgresErrorCodes.CheckViolation,
            expectedConstraint);
    }

    [Fact]
    public async Task SaveChanges_InvalidEvidence_RollsBackWholeAggregate()
    {
        using var cancellation = CreateCancellation();
        Guid draftId;
        await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var (_, posting, skill) = await SeedPrincipalsAsync(
                dbContext,
                cancellation.Token);
            var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
            draftId = draft.Id;
            var draftSkill = CreateExactDraftSkill(skill.Id, 0, "c#");
            draftSkill.Evidence.Add(new JobPostingParseDraftSkillEvidence
            {
                Text = "invalid",
                Start = -1,
                Length = 7
            });
            draft.Skills.Add(draftSkill);
            dbContext.JobPostingParseDrafts.Add(draft);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                dbContext.SaveChangesAsync(cancellation.Token));
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await verification.JobPostingParseDrafts
            .AnyAsync(item => item.Id == draftId, cancellation.Token));
        Assert.False(await verification.JobPostingParseDraftSkills
            .AnyAsync(item => item.JobPostingParseDraftId == draftId, cancellation.Token));
        Assert.False(await verification.JobPostingParseDraftSkillEvidences
            .AnyAsync(cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_ConcurrentDraftUpdates_UsesPostgreSqlXmin()
    {
        using var cancellation = CreateCancellation();
        Guid draftId;
        uint initialVersion;
        await using (var seedContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var (_, posting, _) = await SeedPrincipalsAsync(
                seedContext,
                cancellation.Token);
            var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
            seedContext.JobPostingParseDrafts.Add(draft);
            await seedContext.SaveChangesAsync(cancellation.Token);
            draftId = draft.Id;
            initialVersion = draft.Version;
        }

        await using var contextA = Fixture.DbContextFactory.CreateDbContext();
        await using var contextB = Fixture.DbContextFactory.CreateDbContext();
        var draftA = await contextA.JobPostingParseDrafts.SingleAsync(
            item => item.Id == draftId,
            cancellation.Token);
        var draftB = await contextB.JobPostingParseDrafts.SingleAsync(
            item => item.Id == draftId,
            cancellation.Token);

        draftA.Status = JobPostingParseDraftStatus.Processing;
        draftA.LeaseOwner = "worker-a";
        draftA.LastAttemptAt = SeedTime.AddMinutes(1);
        draftA.LeaseExpiresAt = SeedTime.AddMinutes(2);
        await contextA.SaveChangesAsync(cancellation.Token);

        Assert.NotEqual(initialVersion, draftA.Version);
        draftB.Status = JobPostingParseDraftStatus.Cancelled;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            contextB.SaveChangesAsync(cancellation.Token));
    }

    [Fact]
    public async Task SaveChanges_DeleteResolvedSkill_IsRestrictedAndPreservesDraftHistory()
    {
        using var cancellation = CreateCancellation();
        Guid skillId;
        Guid draftId;
        await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var (_, posting, skill) = await SeedPrincipalsAsync(
                dbContext,
                cancellation.Token);
            skillId = skill.Id;
            var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
            draftId = draft.Id;
            draft.Skills.Add(CreateExactDraftSkill(skill.Id, 0, "c#"));
            dbContext.JobPostingParseDrafts.Add(draft);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        await using (var deleteContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var skill = await deleteContext.Skills.SingleAsync(
                item => item.Id == skillId,
                cancellation.Token);
            deleteContext.Skills.Remove(skill);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                deleteContext.SaveChangesAsync(cancellation.Token));
            AssertPostgres(
                exception,
                PostgresErrorCodes.ForeignKeyViolation,
                SkillForeignKey);
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.True(await verification.JobPostingParseDrafts
            .AnyAsync(item => item.Id == draftId, cancellation.Token));
        Assert.True(await verification.JobPostingParseDraftSkills
            .AnyAsync(
                item => item.JobPostingParseDraftId == draftId,
                cancellation.Token));
    }

    private async Task<DbUpdateException> SaveDuplicateSkillAsync(
        bool duplicateSortOrder,
        CancellationToken cancellationToken)
    {
        using var cancellation = cancellationToken == default
            ? CreateCancellation()
            : null;
        var token = cancellation?.Token ?? cancellationToken;
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (_, posting, skill) = await SeedPrincipalsAsync(dbContext, token);
        var draft = CreateDraft(posting.Id, posting.UserId, posting.Version);
        draft.Skills.Add(CreateExactDraftSkill(skill.Id, 0, "c#"));
        draft.Skills.Add(CreateUnresolvedDraftSkill(
            duplicateSortOrder ? 0 : 1,
            duplicateSortOrder ? "docker" : "c#"));
        dbContext.JobPostingParseDrafts.Add(draft);

        return await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(token));
    }

    private static void ApplyInvalidValue(
        InvalidConstraintCase invalidCase,
        JobPostingParseDraft draft,
        JobPostingParseDraftSkill draftSkill,
        Guid skillId)
    {
        switch (invalidCase)
        {
            case InvalidConstraintCase.DescriptionHash:
                draft.SourceDescriptionHash = "not-a-sha256-hash";
                break;
            case InvalidConstraintCase.ParserVersion:
                draft.ParserVersion = " \t ";
                break;
            case InvalidConstraintCase.SourceVersion:
                draft.SourceJobPostingVersion = 0;
                break;
            case InvalidConstraintCase.AttemptCount:
                draft.AttemptCount = -1;
                break;
            case InvalidConstraintCase.SortOrder:
                draftSkill.SortOrder = -1;
                break;
            case InvalidConstraintCase.ImportanceBelowRange:
                draftSkill.ProposedImportanceScore = -1;
                break;
            case InvalidConstraintCase.ImportanceAboveRange:
                draftSkill.ProposedImportanceScore = 101;
                break;
            case InvalidConstraintCase.ConfidenceBelowRange:
                draftSkill.Confidence = -0.0001m;
                break;
            case InvalidConstraintCase.ConfidenceAboveRange:
                draftSkill.Confidence = 1.0001m;
                break;
            case InvalidConstraintCase.EvidenceStart:
                draftSkill.Evidence.Single().Start = -1;
                break;
            case InvalidConstraintCase.EvidenceLength:
                draftSkill.Evidence.Single().Length = 0;
                break;
            case InvalidConstraintCase.ExactWithoutSkill:
                draftSkill.ResolvedSkillId = null;
                break;
            case InvalidConstraintCase.UnresolvedWithSkill:
                draftSkill.ResolutionStatus = SkillResolutionStatus.Unresolved;
                draftSkill.ResolvedSkillId = skillId;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }
    }

    private static async Task<(User User, JobPosting Posting, Skill Skill)>
        SeedPrincipalsAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var user = CreateUser($"draft-{Guid.NewGuid():N}@example.com");
        var posting = CreateJobPosting(user.Id);
        var skill = new Skill
        {
            Name = "C#",
            NormalizedName = "c#",
            Category = SkillCategory.ProgrammingLanguage
        };
        dbContext.AddRange(user, posting, skill);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (user, posting, skill);
    }

    private static User CreateUser(string email) => new()
    {
        FirstName = "Draft",
        LastName = "Owner",
        Email = email,
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreateJobPosting(Guid userId) => new()
    {
        UserId = userId,
        Title = "Backend Engineer",
        Description = SourceDescription,
        Status = JobPostingStatus.Draft
    };

    private static JobPostingParseDraft CreateDraft(
        Guid jobPostingId,
        Guid userId,
        uint sourceVersion) => new()
    {
        JobPostingId = jobPostingId,
        UserId = userId,
        SourceJobPostingVersion = sourceVersion,
        SourceTitle = "Backend Engineer",
        SourceDescriptionHash = Hash('a'),
        SourceInputHash = SourceInputHashCalculator.Compute(
            "Backend Engineer",
            SourceDescription),
        SourceDescription = SourceDescription,
        ParserVersion = "job-parser-v1",
        ProviderRequestId = "provider-request-1",
        ProviderName = "fake-provider",
        ProviderModel = "fake-model",
        Status = JobPostingParseDraftStatus.Ready,
        AttemptCount = 1,
        RequestedAt = SeedTime,
        CompletedAt = SeedTime.AddSeconds(2)
    };

    private static void AddCompleteSkills(
        JobPostingParseDraft draft,
        Guid resolvedSkillId)
    {
        var exact = CreateExactDraftSkill(resolvedSkillId, 0, "c#");
        exact.Evidence.Add(CreateEvidence("C#", 29));
        var unresolved = CreateUnresolvedDraftSkill(1, "docker");
        unresolved.Evidence.Add(CreateEvidence("Docker", 45));
        draft.Skills.Add(exact);
        draft.Skills.Add(unresolved);
    }

    private static JobPostingParseDraftSkill CreateExactDraftSkill(
        Guid skillId,
        int sortOrder,
        string canonicalKey)
    {
        var draftSkill = new JobPostingParseDraftSkill
        {
            CandidateName = "C#",
            CanonicalKey = canonicalKey,
            ResolvedSkillId = skillId,
            RequirementType = JobSkillRequirementType.Required,
            ProposedImportanceScore = 100,
            Confidence = 0.95m,
            SortOrder = sortOrder,
            ResolutionStatus = SkillResolutionStatus.Exact
        };
        draftSkill.Evidence.Add(CreateEvidence("C#", 9));
        return draftSkill;
    }

    private static JobPostingParseDraftSkill CreateUnresolvedDraftSkill(
        int sortOrder,
        string canonicalKey)
    {
        var draftSkill = new JobPostingParseDraftSkill
        {
            CandidateName = "Docker",
            CanonicalKey = canonicalKey,
            RequirementType = JobSkillRequirementType.Preferred,
            ProposedImportanceScore = 60,
            Confidence = 0.75m,
            SortOrder = sortOrder,
            ResolutionStatus = SkillResolutionStatus.Unresolved
        };
        draftSkill.Evidence.Add(CreateEvidence("Docker", 20));
        return draftSkill;
    }

    private static JobPostingParseDraftSkillEvidence CreateEvidence(
        string text,
        int start) => new()
    {
        Text = text,
        Start = start,
        Length = text.Length
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

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(30));

    private static string Hash(char value) => new(value, 64);

    private const string SourceDescription =
        "We need C# and Docker. C# expertise with Docker is preferred.";

    public enum InvalidConstraintCase
    {
        DescriptionHash,
        ParserVersion,
        SourceVersion,
        AttemptCount,
        SortOrder,
        ImportanceBelowRange,
        ImportanceAboveRange,
        ConfidenceBelowRange,
        ConfidenceAboveRange,
        EvidenceStart,
        EvidenceLength,
        ExactWithoutSkill,
        UnresolvedWithSkill
    }
}
