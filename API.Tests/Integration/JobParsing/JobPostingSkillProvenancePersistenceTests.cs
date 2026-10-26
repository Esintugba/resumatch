using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingSkillProvenancePersistenceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    public JobPostingSkillProvenancePersistenceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Manual_skill_without_source_is_valid()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);

        context.JobPostingSkills.Add(CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.Manual,
            isAiDetected: false));
        await context.SaveChangesAsync(cancellation.Token);

        var persisted = await context.JobPostingSkills.AsNoTracking()
            .SingleAsync(cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.Manual, persisted.ProvenanceStatus);
        Assert.Null(persisted.SourceParseDraftId);
        Assert.Null(persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Legacy_ai_unknown_without_source_is_valid()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);

        context.JobPostingSkills.Add(CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.LegacyAiUnknown,
            isAiDetected: true));
        await context.SaveChangesAsync(cancellation.Token);

        var persisted = await context.JobPostingSkills.AsNoTracking()
            .SingleAsync(cancellation.Token);
        Assert.Equal(
            JobPostingSkillProvenanceStatus.LegacyAiUnknown,
            persisted.ProvenanceStatus);
    }

    [Fact]
    public async Task Ai_active_requires_both_source_identifiers()
    {
        var exception = await SaveInvalidRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus = JobPostingSkillProvenanceStatus.AiActive;
            requirement.IsAiDetected = true;
        });

        AssertConstraint(exception, "CK_JobPostingSkills_ProvenanceInvariant");
    }

    [Fact]
    public async Task Ai_stale_requires_invalidated_timestamp()
    {
        var exception = await SaveInvalidSourcedRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus = JobPostingSkillProvenanceStatus.AiStale;
            requirement.InvalidatedAt = null;
        });

        AssertConstraint(exception, "CK_JobPostingSkills_ProvenanceInvariant");
    }

    [Fact]
    public async Task Ai_manually_adopted_requires_source_identifiers()
    {
        var exception = await SaveInvalidRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus =
                JobPostingSkillProvenanceStatus.AiManuallyAdopted;
            requirement.IsAiDetected = true;
        });

        AssertConstraint(exception, "CK_JobPostingSkills_ProvenanceInvariant");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Partial_null_source_identifiers_are_rejected(bool omitDraft)
    {
        var exception = await SaveInvalidSourcedRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus = JobPostingSkillProvenanceStatus.AiActive;
            if (omitDraft)
            {
                requirement.SourceParseDraftId = null;
            }
            else
            {
                requirement.SourceParseDraftSkillId = null;
            }
        });

        AssertConstraint(exception, "CK_JobPostingSkills_ProvenanceInvariant");
    }

    [Fact]
    public async Task Undefined_provenance_status_is_rejected()
    {
        var exception = await SaveInvalidRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus = (JobPostingSkillProvenanceStatus)999;
        });

        AssertPostgres(exception, PostgresErrorCodes.CheckViolation);
    }

    [Theory]
    [InlineData(JobPostingSkillProvenanceStatus.Manual, true)]
    [InlineData(JobPostingSkillProvenanceStatus.LegacyAiUnknown, false)]
    public async Task IsAiDetected_and_status_mismatch_is_rejected(
        JobPostingSkillProvenanceStatus status,
        bool isAiDetected)
    {
        var exception = await SaveInvalidRequirementAsync(requirement =>
        {
            requirement.ProvenanceStatus = status;
            requirement.IsAiDetected = isAiDetected;
        });

        AssertConstraint(exception, "CK_JobPostingSkills_ProvenanceInvariant");
    }

    [Fact]
    public async Task Candidate_from_another_draft_is_rejected()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var otherDraft = CreateDraft(
            graph.Posting.Id,
            graph.User.Id,
            graph.Posting.Version,
            "Other source");
        var otherCandidate = CreateCandidate(graph.Skill.Id, "c#");
        otherDraft.Skills.Add(otherCandidate);
        context.JobPostingParseDrafts.Add(otherDraft);
        await context.SaveChangesAsync(cancellation.Token);

        var requirement = CreateSourcedRequirement(graph);
        requirement.SourceParseDraftSkillId = otherCandidate.Id;
        context.JobPostingSkills.Add(requirement);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
        AssertPostgres(exception, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Draft_from_another_job_posting_is_rejected()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var otherPosting = CreatePosting(graph.User.Id, "Other posting");
        context.JobPostings.Add(otherPosting);
        await context.SaveChangesAsync(cancellation.Token);
        var otherDraft = CreateDraft(
            otherPosting.Id,
            graph.User.Id,
            otherPosting.Version,
            "Other posting source");
        var otherCandidate = CreateCandidate(graph.Skill.Id, "c#");
        otherDraft.Skills.Add(otherCandidate);
        context.JobPostingParseDrafts.Add(otherDraft);
        await context.SaveChangesAsync(cancellation.Token);

        var requirement = CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.AiActive,
            true);
        requirement.SourceParseDraftId = otherDraft.Id;
        requirement.SourceParseDraftSkillId = otherCandidate.Id;
        context.JobPostingSkills.Add(requirement);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
        AssertPostgres(exception, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Candidate_resolved_skill_must_match_requirement_skill()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var otherSkill = CreateSkill("PostgreSQL", "postgresql");
        context.Skills.Add(otherSkill);
        await context.SaveChangesAsync(cancellation.Token);

        var requirement = CreateSourcedRequirement(graph);
        requirement.SkillId = otherSkill.Id;
        context.JobPostingSkills.Add(requirement);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
        AssertPostgres(
            exception,
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_JobPostingSkills_SourceDraftSkill_Draft_Skill");
    }

    [Fact]
    public async Task Unresolved_candidate_cannot_be_used_as_provenance()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var unresolved = new JobPostingParseDraftSkill
        {
            CandidateName = "Unknown SDK",
            CanonicalKey = "unknown sdk",
            ResolvedSkillId = null,
            RequirementType = JobSkillRequirementType.Mentioned,
            ProposedImportanceScore = 20,
            SortOrder = 1,
            ResolutionStatus = SkillResolutionStatus.Unresolved
        };
        graph.Draft.Skills.Add(unresolved);
        await context.SaveChangesAsync(cancellation.Token);

        var requirement = CreateSourcedRequirement(graph);
        requirement.SourceParseDraftSkillId = unresolved.Id;
        context.JobPostingSkills.Add(requirement);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
        AssertPostgres(
            exception,
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_JobPostingSkills_SourceDraftSkill_Draft_Skill");
    }

    [Fact]
    public async Task Same_candidate_cannot_be_linked_twice()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var first = CreateSourcedRequirement(graph);
        context.JobPostingSkills.Add(first);
        await context.SaveChangesAsync(cancellation.Token);

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellation.Token);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "JobPostingSkills"
                ("Id", "JobPostingId", "SkillId", "RequirementType",
                 "ImportanceScore", "IsAiDetected", "ProvenanceStatus",
                 "SourceParseDraftId", "SourceParseDraftSkillId", "CreatedAt")
            VALUES
                (@id, @postingId, @skillId, 0, 90, TRUE, 1,
                 @draftId, @candidateId, @createdAt);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("postingId", graph.Posting.Id);
        command.Parameters.AddWithValue("skillId", graph.Skill.Id);
        command.Parameters.AddWithValue("draftId", graph.Draft.Id);
        command.Parameters.AddWithValue("candidateId", graph.Candidate.Id);
        command.Parameters.AddWithValue("createdAt", Now);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellation.Token));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Existing_job_posting_skill_uniqueness_is_preserved()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        context.JobPostingSkills.Add(CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.Manual,
            false));
        await context.SaveChangesAsync(cancellation.Token);

        context.JobPostingSkills.Add(CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.Manual,
            false));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
        AssertPostgres(exception, PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Skill_delete_remains_restricted()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        context.JobPostingSkills.Add(CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.Manual,
            false));
        await context.SaveChangesAsync(cancellation.Token);

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellation.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM \"Skills\" WHERE \"Id\" = @id;";
        command.Parameters.AddWithValue("id", graph.Skill.Id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellation.Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sourced_provenance_restricts_candidate_and_draft_delete(
        bool deleteCandidate)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        context.JobPostingSkills.Add(CreateSourcedRequirement(graph));
        await context.SaveChangesAsync(cancellation.Token);

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(cancellation.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = deleteCandidate
            ? "DELETE FROM \"JobPostingParseDraftSkills\" WHERE \"Id\" = @id;"
            : "DELETE FROM \"JobPostingParseDrafts\" WHERE \"Id\" = @id;";
        command.Parameters.AddWithValue(
            "id",
            deleteCandidate ? graph.Candidate.Id : graph.Draft.Id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(cancellation.Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    private async Task<DbUpdateException> SaveInvalidRequirementAsync(
        Action<JobPostingSkill> mutate)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var requirement = CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.Manual,
            false);
        mutate(requirement);
        context.JobPostingSkills.Add(requirement);
        return await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
    }

    private async Task<DbUpdateException> SaveInvalidSourcedRequirementAsync(
        Action<JobPostingSkill> mutate)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var graph = await SeedGraphAsync(context, cancellation.Token);
        var requirement = CreateSourcedRequirement(graph);
        mutate(requirement);
        context.JobPostingSkills.Add(requirement);
        return await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));
    }

    private static async Task<SeedGraph> SeedGraphAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            FirstName = "Provenance",
            LastName = "Owner",
            Email = $"provenance-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var posting = CreatePosting(user.Id, "Backend Engineer");
        var skill = CreateSkill("C#", "c#");
        context.AddRange(user, posting, skill);
        await context.SaveChangesAsync(cancellationToken);

        var draft = CreateDraft(
            posting.Id,
            user.Id,
            posting.Version,
            "Primary source");
        var candidate = CreateCandidate(skill.Id, "c#");
        draft.Skills.Add(candidate);
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);
        return new SeedGraph(user, posting, skill, draft, candidate);
    }

    private static JobPosting CreatePosting(Guid userId, string title) => new()
    {
        UserId = userId,
        Title = title,
        Description = "C# is required.",
        Status = JobPostingStatus.Draft
    };

    private static Skill CreateSkill(string name, string normalizedName) => new()
    {
        Name = name,
        NormalizedName = normalizedName,
        Category = SkillCategory.ProgrammingLanguage
    };

    private static JobPostingParseDraft CreateDraft(
        Guid postingId,
        Guid userId,
        uint version,
        string discriminator)
    {
        var title = $"Backend Engineer {discriminator}";
        const string description = "C# is required.";
        return new JobPostingParseDraft
        {
            JobPostingId = postingId,
            UserId = userId,
            SourceJobPostingVersion = version,
            SourceTitle = title,
            SourceDescription = description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(description),
            SourceInputHash = SourceInputHashCalculator.Compute(title, description),
            ParserVersion = "job-parser-v1",
            Status = JobPostingParseDraftStatus.Confirmed,
            AttemptCount = 1,
            RequestedAt = Now,
            CompletedAt = Now,
            ConfirmedAt = Now,
            ExpiresAt = Now.AddHours(1)
        };
    }

    private static JobPostingParseDraftSkill CreateCandidate(
        Guid skillId,
        string canonicalKey) => new()
    {
        CandidateName = "C#",
        CanonicalKey = canonicalKey,
        ResolvedSkillId = skillId,
        RequirementType = JobSkillRequirementType.Required,
        ProposedImportanceScore = 90,
        Confidence = 0.95m,
        SortOrder = 0,
        ResolutionStatus = SkillResolutionStatus.Exact
    };

    private static JobPostingSkill CreateRequirement(
        Guid postingId,
        Guid skillId,
        JobPostingSkillProvenanceStatus status,
        bool isAiDetected) => new()
    {
        JobPostingId = postingId,
        SkillId = skillId,
        RequirementType = JobSkillRequirementType.Required,
        ImportanceScore = 90,
        IsAiDetected = isAiDetected,
        ProvenanceStatus = status
    };

    private static JobPostingSkill CreateSourcedRequirement(SeedGraph graph)
    {
        var requirement = CreateRequirement(
            graph.Posting.Id,
            graph.Skill.Id,
            JobPostingSkillProvenanceStatus.AiActive,
            true);
        requirement.SourceParseDraftId = graph.Draft.Id;
        requirement.SourceParseDraftSkillId = graph.Candidate.Id;
        return requirement;
    }

    private static void AssertConstraint(
        DbUpdateException exception,
        string constraintName) =>
        AssertPostgres(
            exception,
            PostgresErrorCodes.CheckViolation,
            constraintName);

    private static void AssertPostgres(
        DbUpdateException exception,
        string sqlState,
        string? constraintName = null)
    {
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, postgres.ConstraintName);
        }
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeedGraph(
        User User,
        JobPosting Posting,
        Skill Skill,
        JobPostingParseDraft Draft,
        JobPostingParseDraftSkill Candidate);
}
