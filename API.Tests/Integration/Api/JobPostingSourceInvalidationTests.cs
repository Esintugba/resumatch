using System.Net;
using System.Net.Http.Json;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Application.JobPostings;
using Api.Data;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace API.Tests.Integration.Api;

public sealed class JobPostingSourceInvalidationTests : ApiIntegrationTestBase
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    public JobPostingSourceInvalidationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("both")]
    [InlineData("whitespace")]
    [InlineData("punctuation")]
    public async Task Source_change_invalidates_ai_active_requirement_atomically(
        string mutation)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var request = CreateUpdateRequest(seed.Posting, mutation);
        using var client = CreateClient(seed.User);

        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            request,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiStale,
            persisted.ProvenanceStatus);
        Assert.NotNull(persisted.InvalidatedAt);
        Assert.Equal(TimeSpan.Zero, persisted.InvalidatedAt.Value.Offset);
        Assert.Equal(seed.Requirements[0].SourceParseDraftId,
            persisted.SourceParseDraftId);
        Assert.Equal(seed.Requirements[0].SourceParseDraftSkillId,
            persisted.SourceParseDraftSkillId);
        Assert.Equal(seed.Requirements[0].RequirementType,
            persisted.RequirementType);
        Assert.Equal(seed.Requirements[0].ImportanceScore,
            persisted.ImportanceScore);
        Assert.True(persisted.IsAiDetected);

        var posting = await verification.JobPostings
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Posting.Id,
                cancellation.Token);
        Assert.Null(posting.ParsedSummary);
    }

    [Fact]
    public async Task Non_source_update_preserves_active_requirement_and_parsed_summary()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var request = CreateUpdateRequest(seed.Posting, "company");
        using var client = CreateClient(seed.User);

        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            request,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            requirement.ProvenanceStatus);
        Assert.Null(requirement.InvalidatedAt);
        var summary = await verification.JobPostings
            .AsNoTracking()
            .Where(item => item.Id == seed.Posting.Id)
            .Select(item => item.ParsedSummary)
            .SingleAsync(cancellation.Token);
        Assert.Equal(seed.Posting.ParsedSummary, summary);
    }

    [Fact]
    public async Task Saving_identical_source_does_not_invalidate_or_clear_summary()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var request = CreateUpdateRequest(seed.Posting, "none");
        using var client = CreateClient(seed.User);

        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            request,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            requirement.ProvenanceStatus);
        Assert.Null(requirement.InvalidatedAt);
        Assert.Equal(seed.Posting.ParsedSummary,
            (await verification.JobPostings.AsNoTracking().SingleAsync(
                item => item.Id == seed.Posting.Id,
                cancellation.Token)).ParsedSummary);
    }

    [Fact]
    public async Task Source_change_only_invalidates_ai_active_status()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var before = seed.Requirements.ToDictionary(item => item.Id);
        using var client = CreateClient(seed.User);

        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            CreateUpdateRequest(seed.Posting, "description"),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .OrderBy(item => item.SkillId)
            .ToListAsync(cancellation.Token);
        Assert.Equal(5, persisted.Count);
        foreach (var requirement in persisted)
        {
            var original = before[requirement.Id];
            var expectedStatus = original.ProvenanceStatus ==
                JobPostingSkillProvenanceStatus.AiActive
                    ? JobPostingSkillProvenanceStatus.AiStale
                    : original.ProvenanceStatus;
            Assert.Equal(expectedStatus, requirement.ProvenanceStatus);
            Assert.Equal(original.SourceParseDraftId,
                requirement.SourceParseDraftId);
            Assert.Equal(original.SourceParseDraftSkillId,
                requirement.SourceParseDraftSkillId);
            Assert.Equal(original.RequirementType, requirement.RequirementType);
            Assert.Equal(original.ImportanceScore, requirement.ImportanceScore);
            Assert.Equal(original.IsAiDetected, requirement.IsAiDetected);

            if (original.ProvenanceStatus ==
                JobPostingSkillProvenanceStatus.AiStale)
            {
                Assert.Equal(original.InvalidatedAt, requirement.InvalidatedAt);
            }
            else if (original.ProvenanceStatus !=
                     JobPostingSkillProvenanceStatus.AiActive)
            {
                Assert.Null(requirement.InvalidatedAt);
            }
        }
    }

    [Fact]
    public async Task Stale_etag_returns_precondition_failed_without_invalidation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        using var client = CreateClient(seed.User);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/job-postings/{seed.Posting.Id:D}")
        {
            Content = JsonContent.Create(
                CreateUpdateRequest(seed.Posting, "description"))
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(DifferentVersion(seed.Posting.Version)));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            requirement.ProvenanceStatus);
    }

    [Fact]
    public async Task Cross_user_update_returns_not_found_without_invalidation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        User otherUser;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            otherUser = CreateUser("other-owner");
            context.Users.Add(otherUser);
            await context.SaveChangesAsync(cancellation.Token);
        }

        using var client = CreateClient(otherUser);
        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            CreateUpdateRequest(seed.Posting, "description"),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            requirement.ProvenanceStatus);
    }

    [Fact]
    public async Task Concurrent_source_update_and_confirmation_produce_consistent_result()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        JobPostingParseDraft readyDraft;
        JobPostingParseDraftSkill readyCandidate;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            readyDraft = new JobPostingParseDraft
            {
                JobPostingId = seed.Posting.Id,
                UserId = seed.User.Id,
                SourceJobPostingVersion = seed.Posting.Version,
                SourceTitle = seed.Posting.Title,
                SourceDescription = seed.Posting.Description,
                SourceDescriptionHash = DescriptionHashCalculator.Compute(
                    seed.Posting.Description),
                SourceInputHash = SourceInputHashCalculator.Compute(
                    seed.Posting.Title,
                    seed.Posting.Description),
                SkillCatalogHash = seed.Draft.SkillCatalogHash,
                ParserVersion = "job-parser-v1",
                Status = JobPostingParseDraftStatus.Ready,
                RequestedAt = SeedTime,
                CompletedAt = SeedTime.AddMinutes(1),
                ExpiresAt = SeedTime.AddYears(1),
                CreatedAt = SeedTime
            };
            readyCandidate = new JobPostingParseDraftSkill
            {
                CandidateName = "Source Skill 0",
                CanonicalKey = "source-skill-0",
                ResolvedSkillId = seed.Requirements[0].SkillId,
                RequirementType = JobSkillRequirementType.Required,
                ProposedImportanceScore = 90,
                Confidence = 0.9000m,
                SortOrder = 0,
                ResolutionStatus = SkillResolutionStatus.Exact,
                CreatedAt = SeedTime
            };
            readyDraft.Skills.Add(readyCandidate);
            context.JobPostingParseDrafts.Add(readyDraft);
            await context.SaveChangesAsync(cancellation.Token);
        }

        using var updateClient = CreateClient(seed.User);
        using var confirmationClient = CreateClient(seed.User);
        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/job-postings/{seed.Posting.Id:D}")
        {
            Content = JsonContent.Create(
                CreateUpdateRequest(seed.Posting, "description"))
        };
        updateRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(seed.Posting.Version));
        using var confirmRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{seed.Posting.Id:D}/parse-drafts/{readyDraft.Id:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmJobPostingParseDraftRequest
            {
                SelectedSkills =
                    [new() { DraftSkillId = readyCandidate.Id }]
            })
        };
        confirmRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(readyDraft.Version));

        var responses = await Task.WhenAll(
            updateClient.SendAsync(updateRequest, cancellation.Token),
            confirmationClient.SendAsync(confirmRequest, cancellation.Token));
        try
        {
            Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
            Assert.Contains(
                responses[1].StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.SkillId == seed.Requirements[0].SkillId,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiStale,
            requirement.ProvenanceStatus);
        Assert.Equal(1, await verification.JobPostingSkills
            .AsNoTracking()
            .CountAsync(item => item.JobPostingId == seed.Posting.Id &&
                item.SkillId == seed.Requirements[0].SkillId,
                cancellation.Token));
        var persistedDraft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == readyDraft.Id,
                cancellation.Token);
        Assert.Contains(
            persistedDraft.Status,
            new[]
            {
                JobPostingParseDraftStatus.Confirmed,
                JobPostingParseDraftStatus.Superseded
            });
    }

    [Fact]
    public async Task Update_failure_rolls_back_source_summary_and_invalidation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(new InvalidationFailureInterceptor())
            .Options;
        await using var context = new AppDbContext(options);
        var service = new JobPostingService(
            context,
            new FixedCurrentUserService(seed.User.Id),
            new FixedTimeProvider(SeedTime.AddHours(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UpdateAsync(
            seed.Posting.Id,
            seed.Posting.Version,
            CreateUpdateRequest(seed.Posting, "description"),
            cancellation.Token));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var posting = await verification.JobPostings
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Posting.Id,
                cancellation.Token);
        Assert.Equal(seed.Posting.Description, posting.Description);
        Assert.Equal(seed.Posting.ParsedSummary, posting.ParsedSummary);
        var requirement = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirements[0].Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            requirement.ProvenanceStatus);
        Assert.Null(requirement.InvalidatedAt);
    }

    [Fact]
    public async Task Analysis_loader_includes_only_active_provenance_states()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var loader = new AnalysisInputLoader(context);

        var input = await loader.LoadAsync(
            seed.Resume.Id,
            seed.Posting.Id,
            seed.User.Id,
            SeedTime.AddHours(2),
            cancellation.Token);

        Assert.NotNull(input.JobPosting);
        var expectedSkillIds = seed.Requirements
            .Where(item => item.ProvenanceStatus is
                JobPostingSkillProvenanceStatus.Manual or
                JobPostingSkillProvenanceStatus.AiActive or
                JobPostingSkillProvenanceStatus.AiManuallyAdopted)
            .Select(item => item.SkillId)
            .Order()
            .ToArray();
        Assert.Equal(
            expectedSkillIds,
            input.JobPosting.Skills.Select(item => item.Skill.Id).Order().ToArray());

        var engine = new AnalysisEngineV1();
        var withJob = engine.Analyze(input);
        var withoutJobInput = await loader.LoadAsync(
            seed.Resume.Id,
            null,
            seed.User.Id,
            SeedTime.AddHours(2),
            cancellation.Token);
        var withoutJob = engine.Analyze(withoutJobInput);
        Assert.Equal(100m, withJob.JobMatchScore);
        Assert.Equal(withoutJob.AtsScore, withJob.AtsScore);
    }

    [Fact]
    public async Task Source_update_preserves_confirmed_draft_candidate_and_evidence_history()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedGraphAsync(cancellation.Token);
        var candidateIds = seed.Candidates.Select(item => item.Id).Order().ToArray();
        using var client = CreateClient(seed.User);

        using var response = await SendUpdateAsync(
            client,
            seed.Posting,
            CreateUpdateRequest(seed.Posting, "title"),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.True(await verification.JobPostingParseDrafts
            .AsNoTracking()
            .AnyAsync(item => item.Id == seed.Draft.Id &&
                item.Status == JobPostingParseDraftStatus.Confirmed,
                cancellation.Token));
        Assert.Equal(candidateIds, await verification.JobPostingParseDraftSkills
            .AsNoTracking()
            .Where(item => item.JobPostingParseDraftId == seed.Draft.Id)
            .Select(item => item.Id)
            .OrderBy(item => item)
            .ToArrayAsync(cancellation.Token));
        Assert.Equal(seed.Candidates.Count, await verification
            .JobPostingParseDraftSkillEvidences
            .AsNoTracking()
            .CountAsync(cancellation.Token));
    }

    private async Task<SeededGraph> SeedGraphAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser("source-owner");
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            CompanyName = "Original Company",
            Location = "Istanbul",
            Description = "Build reliable APIs with C# and PostgreSQL",
            ParsedSummary = "Previously parsed summary",
            Status = JobPostingStatus.Analyzed,
            CreatedAt = SeedTime
        };
        var statuses = new[]
        {
            JobPostingSkillProvenanceStatus.AiActive,
            JobPostingSkillProvenanceStatus.Manual,
            JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            JobPostingSkillProvenanceStatus.AiStale,
            JobPostingSkillProvenanceStatus.LegacyAiUnknown
        };
        var skills = statuses.Select((_, index) => new Skill
        {
            Name = $"Source Skill {index}",
            NormalizedName = $"source-skill-{index}",
            Category = SkillCategory.Backend,
            CreatedAt = SeedTime
        }).ToList();
        context.AddRange(user, posting);
        context.Skills.AddRange(skills);
        await context.SaveChangesAsync(cancellationToken);

        var catalog = skills.Select(skill => new SkillCatalogItem(
            skill.Id,
            skill.Name,
            skill.NormalizedName,
            skill.Category));
        var draft = new JobPostingParseDraft
        {
            JobPostingId = posting.Id,
            UserId = user.Id,
            SourceJobPostingVersion = posting.Version,
            SourceTitle = posting.Title,
            SourceDescription = posting.Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(
                posting.Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                posting.Title,
                posting.Description),
            SkillCatalogHash = SkillCatalogHashCalculator.Compute(catalog),
            ParserVersion = "job-parser-v1",
            Status = JobPostingParseDraftStatus.Confirmed,
            RequestedAt = SeedTime.AddMinutes(-3),
            CompletedAt = SeedTime.AddMinutes(-2),
            ConfirmedAt = SeedTime.AddMinutes(-1),
            ExpiresAt = SeedTime.AddHours(1),
            CreatedAt = SeedTime.AddMinutes(-3)
        };
        var candidates = new List<JobPostingParseDraftSkill>();
        for (var index = 0; index < skills.Count; index++)
        {
            var candidate = new JobPostingParseDraftSkill
            {
                CandidateName = skills[index].Name,
                CanonicalKey = skills[index].NormalizedName!,
                ResolvedSkillId = skills[index].Id,
                RequirementType = JobSkillRequirementType.Required,
                ProposedImportanceScore = 80 - index,
                Confidence = 0.9000m,
                SortOrder = index,
                ResolutionStatus = SkillResolutionStatus.Exact,
                CreatedAt = SeedTime
            };
            candidate.Evidence.Add(new JobPostingParseDraftSkillEvidence
            {
                Text = skills[index].Name,
                Start = index * 20,
                Length = skills[index].Name.Length,
                CreatedAt = SeedTime
            });
            draft.Skills.Add(candidate);
            candidates.Add(candidate);
        }

        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        var requirements = new List<JobPostingSkill>();
        for (var index = 0; index < statuses.Length; index++)
        {
            var status = statuses[index];
            var hasSource = status is
                JobPostingSkillProvenanceStatus.AiActive or
                JobPostingSkillProvenanceStatus.AiStale or
                JobPostingSkillProvenanceStatus.AiManuallyAdopted;
            requirements.Add(new JobPostingSkill
            {
                JobPostingId = posting.Id,
                SkillId = skills[index].Id,
                RequirementType = JobSkillRequirementType.Required,
                ImportanceScore = 80 - index,
                IsAiDetected = status != JobPostingSkillProvenanceStatus.Manual,
                ProvenanceStatus = status,
                SourceParseDraftId = hasSource ? draft.Id : null,
                SourceParseDraftSkillId = hasSource ? candidates[index].Id : null,
                InvalidatedAt = status == JobPostingSkillProvenanceStatus.AiStale
                    ? SeedTime.AddMinutes(-10)
                    : null,
                CreatedAt = SeedTime
            });
        }

        context.JobPostingSkills.AddRange(requirements);
        var resume = new Resume
        {
            UserId = user.Id,
            Title = "Source filtering resume",
            ProfessionalTitle = "Backend Engineer",
            IsMasterResume = true,
            CreatedAt = SeedTime
        };
        context.Resumes.Add(resume);
        foreach (var index in new[] { 0, 1, 2 })
        {
            context.ResumeSkills.Add(new ResumeSkill
            {
                ResumeId = resume.Id,
                SkillId = skills[index].Id,
                SortOrder = index,
                CreatedAt = SeedTime
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        return new SeededGraph(
            user,
            posting,
            draft,
            candidates,
            requirements,
            resume);
    }

    private HttpClient CreateClient(User user) =>
        new AuthenticatedHttpClientFactory(Application)
            .CreateForExistingUser(user);

    private static UpdateJobPostingRequest CreateUpdateRequest(
        JobPosting posting,
        string mutation)
    {
        return new UpdateJobPostingRequest
        {
            Title = mutation is "title" or "both"
                ? posting.Title + " II"
                : posting.Title,
            CompanyName = mutation == "company"
                ? "Changed Company"
                : posting.CompanyName,
            Location = posting.Location,
            WorkType = posting.WorkType,
            EmploymentType = posting.EmploymentType,
            Description = mutation switch
            {
                "description" or "both" => posting.Description + " and Docker",
                "whitespace" => posting.Description + " ",
                "punctuation" => posting.Description + ".",
                _ => posting.Description
            },
            SourceUrl = posting.SourceUrl,
            PublishedAt = posting.PublishedAt
        };
    }

    private static async Task<HttpResponseMessage> SendUpdateAsync(
        HttpClient client,
        JobPosting posting,
        UpdateJobPostingRequest requestBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/job-postings/{posting.Id:D}")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(posting.Version));
        return await client.SendAsync(request, cancellationToken);
    }

    private static User CreateUser(string prefix) => new()
    {
        FirstName = "Source",
        LastName = "Owner",
        Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true,
        CreatedAt = SeedTime
    };

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeededGraph(
        User User,
        JobPosting Posting,
        JobPostingParseDraft Draft,
        IReadOnlyList<JobPostingParseDraftSkill> Candidates,
        IReadOnlyList<JobPostingSkill> Requirements,
        Resume Resume);

    private sealed class FixedCurrentUserService(Guid userId) :
        ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => "source-owner@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InvalidationFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<JobPostingSkill>()
                    .Any(entry =>
                        entry.State == EntityState.Modified &&
                        entry.Entity.ProvenanceStatus ==
                            JobPostingSkillProvenanceStatus.AiStale) == true)
            {
                throw new DbUpdateException(
                    "Controlled invalidation persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
