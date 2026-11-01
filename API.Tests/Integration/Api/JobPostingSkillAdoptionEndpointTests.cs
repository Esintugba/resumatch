using System.Net;
using System.Net.Http.Json;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Preprocessing;
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

public sealed class JobPostingSkillAdoptionEndpointTests : ApiIntegrationTestBase
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 2, 20, 12, 0, 0, TimeSpan.Zero);

    public JobPostingSkillAdoptionEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Ai_active_skill_can_be_manually_adopted_with_safe_provenance()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendAdoptAsync(
            client,
            seed,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            ApiJson.Options,
            cancellation.Token);
        Assert.NotNull(body);
        Assert.NotEqual(EntityTagCodec.Format(seed.Posting.Version), body.ETag);
        Assert.Equal(body.ETag, response.Headers.ETag?.Tag);
        var skill = Assert.Single(body.Skills);
        AssertSafeAdoptedResponse(skill, seed);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            persisted.ProvenanceStatus);
        Assert.Null(persisted.InvalidatedAt);
        Assert.True(persisted.IsAiDetected);
        Assert.Equal(seed.Draft!.Id, persisted.SourceParseDraftId);
        Assert.Equal(seed.Candidate!.Id, persisted.SourceParseDraftSkillId);
        Assert.Equal(seed.Requirement.RequirementType, persisted.RequirementType);
        Assert.Equal(seed.Requirement.ImportanceScore, persisted.ImportanceScore);
    }

    [Fact]
    public async Task Ai_stale_skill_can_be_adopted_and_clears_invalidation_time()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiStale,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendAdoptAsync(
            client,
            seed,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            persisted.ProvenanceStatus);
        Assert.Null(persisted.InvalidatedAt);
        Assert.Equal(seed.Draft!.Id, persisted.SourceParseDraftId);
        Assert.Equal(seed.Candidate!.Id, persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Already_adopted_skill_is_an_idempotent_operation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            cancellation.Token);
        var originalEtag = EntityTagCodec.Format(seed.Posting.Version);
        using var client = CreateClient(seed.User);

        using var response = await SendAdoptAsync(
            client,
            seed,
            originalEtag,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            ApiJson.Options,
            cancellation.Token);
        Assert.NotNull(body);
        Assert.Equal(originalEtag, body.ETag);
        Assert.Equal(originalEtag, response.Headers.ETag?.Tag);
    }

    [Theory]
    [InlineData(JobPostingSkillProvenanceStatus.Manual)]
    [InlineData(JobPostingSkillProvenanceStatus.LegacyAiUnknown)]
    public async Task Non_source_provenance_cannot_be_adopted(
        JobPostingSkillProvenanceStatus status)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(status, cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendAdoptAsync(
            client,
            seed,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Equal(status, persisted.ProvenanceStatus);
    }

    [Fact]
    public async Task Adoption_requires_authentication_and_if_match()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        using var anonymous = Application.CreateHttpsClient();
        using var authenticated = CreateClient(seed.User);

        using var anonymousResponse = await SendAdoptAsync(
            anonymous,
            seed,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);
        using var missingHeaderResponse = await SendAdoptAsync(
            authenticated,
            seed,
            null,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal((HttpStatusCode)428, missingHeaderResponse.StatusCode);
    }

    [Fact]
    public async Task Malformed_or_stale_etag_does_not_mutate_skill()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var malformed = await SendAdoptAsync(
            client,
            seed,
            "not-an-etag",
            cancellation.Token);
        using var stale = await SendAdoptAsync(
            client,
            seed,
            EntityTagCodec.Format(DifferentVersion(seed.Posting.Version)),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            persisted.ProvenanceStatus);
    }

    [Fact]
    public async Task Cross_user_adoption_returns_not_found_without_disclosure()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        User otherUser;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            otherUser = CreateUser("other-owner");
            context.Users.Add(otherUser);
            await context.SaveChangesAsync(cancellation.Token);
        }
        using var client = CreateClient(otherUser);

        using var response = await SendAdoptAsync(
            client,
            seed,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_skill_returns_not_found_without_mutation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var request = CreateAdoptRequest(
            seed.Posting.Id,
            Guid.NewGuid(),
            EntityTagCodec.Format(seed.Posting.Version));
        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_job_posting_exposes_only_safe_skill_provenance_metadata()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiStale,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await client.GetAsync(
            $"/api/job-postings/{seed.Posting.Id:D}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            ApiJson.Options,
            cancellation.Token);
        Assert.NotNull(body);
        var skill = Assert.Single(body.Skills);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiStale,
            skill.ProvenanceStatus);
        Assert.Equal(seed.Draft!.Id, skill.SourceParseDraftId);
        Assert.Equal(seed.Candidate!.Id, skill.SourceParseDraftSkillId);
        Assert.Equal(seed.Requirement.InvalidatedAt, skill.InvalidatedAt);
    }

    [Fact]
    public async Task Concurrent_source_update_and_adoption_allow_only_one_stale_writer()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        var etag = EntityTagCodec.Format(seed.Posting.Version);
        using var updateClient = CreateClient(seed.User);
        using var adoptionClient = CreateClient(seed.User);
        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/job-postings/{seed.Posting.Id:D}")
        {
            Content = JsonContent.Create(new UpdateJobPostingRequest
            {
                Title = seed.Posting.Title,
                Description = seed.Posting.Description + " changed"
            })
        };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        using var adoptRequest = CreateAdoptRequest(
            seed.Posting.Id,
            seed.Skill.Id,
            etag);

        var responses = await Task.WhenAll(
            updateClient.SendAsync(updateRequest, cancellation.Token),
            adoptionClient.SendAsync(adoptRequest, cancellation.Token));
        try
        {
            Assert.Equal(1, responses.Count(item =>
                item.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(item =>
                item.StatusCode == HttpStatusCode.PreconditionFailed));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Contains(
            persisted.ProvenanceStatus,
            new[]
            {
                JobPostingSkillProvenanceStatus.AiStale,
                JobPostingSkillProvenanceStatus.AiManuallyAdopted
            });
        Assert.Equal(seed.Draft!.Id, persisted.SourceParseDraftId);
        Assert.Equal(seed.Candidate!.Id, persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_skill_and_parent_version()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(new AdoptionFailureInterceptor())
            .Options;
        await using var context = new AppDbContext(options);
        var service = new JobPostingService(
            context,
            new FixedCurrentUserService(seed.User.Id),
            new FixedTimeProvider(SeedTime.AddHours(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.AdoptSkillAsync(
                seed.Posting.Id,
                seed.Skill.Id,
                seed.Posting.Version,
                cancellation.Token));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persistedPosting = await verification.JobPostings
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Posting.Id,
                cancellation.Token);
        var persistedSkill = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Requirement.Id,
                cancellation.Token);
        Assert.Equal(seed.Posting.Version, persistedPosting.Version);
        Assert.Equal(seed.Posting.UpdatedAt, persistedPosting.UpdatedAt);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive,
            persistedSkill.ProvenanceStatus);
        Assert.Null(persistedSkill.InvalidatedAt);
    }

    private async Task<AdoptionSeed> SeedAsync(
        JobPostingSkillProvenanceStatus status,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser("adoption-owner");
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            Description = "C# and ASP.NET Core are required.",
            Status = JobPostingStatus.Analyzed,
            ParsedSummary = "Backend role"
        };
        var skill = new Skill
        {
            Name = "ASP.NET Core",
            NormalizedName = "asp.net core",
            Category = SkillCategory.Backend
        };
        context.AddRange(user, posting, skill);
        await context.SaveChangesAsync(cancellationToken);

        JobPostingParseDraft? draft = null;
        JobPostingParseDraftSkill? candidate = null;
        var hasSource = status is
            JobPostingSkillProvenanceStatus.AiActive or
            JobPostingSkillProvenanceStatus.AiStale or
            JobPostingSkillProvenanceStatus.AiManuallyAdopted;
        if (hasSource)
        {
            draft = new JobPostingParseDraft
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
                ParserVersion = "job-parser-v1",
                Status = JobPostingParseDraftStatus.Confirmed,
                RequestedAt = SeedTime,
                CompletedAt = SeedTime.AddMinutes(1),
                ConfirmedAt = SeedTime.AddMinutes(2),
                ExpiresAt = SeedTime.AddDays(1)
            };
            candidate = new JobPostingParseDraftSkill
            {
                CandidateName = skill.Name,
                CanonicalKey = skill.NormalizedName!,
                ResolvedSkillId = skill.Id,
                RequirementType = JobSkillRequirementType.Required,
                ProposedImportanceScore = 92,
                Confidence = 0.9500m,
                SortOrder = 0,
                ResolutionStatus = SkillResolutionStatus.Exact
            };
            draft.Skills.Add(candidate);
            context.JobPostingParseDrafts.Add(draft);
            await context.SaveChangesAsync(cancellationToken);
        }

        var requirement = new JobPostingSkill
        {
            JobPostingId = posting.Id,
            SkillId = skill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 92,
            IsAiDetected = status != JobPostingSkillProvenanceStatus.Manual,
            ProvenanceStatus = status,
            SourceParseDraftId = draft?.Id,
            SourceParseDraftSkillId = candidate?.Id,
            InvalidatedAt = status == JobPostingSkillProvenanceStatus.AiStale
                ? SeedTime.AddMinutes(30)
                : null
        };
        context.JobPostingSkills.Add(requirement);
        await context.SaveChangesAsync(cancellationToken);

        return new AdoptionSeed(user, posting, skill, requirement, draft, candidate);
    }

    private HttpClient CreateClient(User user) =>
        new AuthenticatedHttpClientFactory(Application)
            .CreateForExistingUser(user);

    private static Task<HttpResponseMessage> SendAdoptAsync(
        HttpClient client,
        AdoptionSeed seed,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var request = CreateAdoptRequest(
            seed.Posting.Id,
            seed.Skill.Id,
            ifMatch);
        return SendAndDisposeRequestAsync(client, request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            return await client.SendAsync(request, cancellationToken);
        }
    }

    private static HttpRequestMessage CreateAdoptRequest(
        Guid postingId,
        Guid skillId,
        string? ifMatch)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{postingId:D}/skills/{skillId:D}/adopt");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    private static void AssertSafeAdoptedResponse(
        JobPostingSkillResponse response,
        AdoptionSeed seed)
    {
        Assert.Equal(seed.Skill.Id, response.SkillId);
        Assert.Equal(seed.Skill.Name, response.SkillName);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            response.ProvenanceStatus);
        Assert.Equal(seed.Draft?.Id, response.SourceParseDraftId);
        Assert.Equal(seed.Candidate?.Id, response.SourceParseDraftSkillId);
        Assert.Null(response.InvalidatedAt);
        Assert.True(response.IsAiDetected);
        Assert.Equal(seed.Requirement.RequirementType, response.RequirementType);
        Assert.Equal(seed.Requirement.ImportanceScore, response.ImportanceScore);
    }

    private static User CreateUser(string prefix) => new()
    {
        FirstName = "Adoption",
        LastName = "Owner",
        Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record AdoptionSeed(
        User User,
        JobPosting Posting,
        Skill Skill,
        JobPostingSkill Requirement,
        JobPostingParseDraft? Draft,
        JobPostingParseDraftSkill? Candidate);

    private sealed class FixedCurrentUserService(Guid userId) :
        ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => "adoption@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdoptionFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<JobPostingSkill>()
                    .Any(entry => entry.State == EntityState.Modified) == true)
            {
                throw new DbUpdateException(
                    "Controlled adoption persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
