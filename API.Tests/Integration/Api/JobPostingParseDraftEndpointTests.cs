using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Application.Common.Concurrency;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class JobPostingParseDraftEndpointTests : ApiIntegrationTestBase
{
    private const string ParserVersion = "job-parser-v1";
    private const string Description =
        "Strong experience with C# and ASP.NET Core is required.";

    public JobPostingParseDraftEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData("POST", "/api/job-postings/11111111-1111-1111-1111-111111111111/parse")]
    [InlineData("GET", "/api/job-postings/11111111-1111-1111-1111-111111111111/parse-drafts/22222222-2222-2222-2222-222222222222")]
    [InlineData("GET", "/api/job-postings/11111111-1111-1111-1111-111111111111/parse-drafts")]
    [InlineData("POST", "/api/job-postings/11111111-1111-1111-1111-111111111111/parse-drafts/22222222-2222-2222-2222-222222222222/cancel")]
    public async Task Draft_endpoints_without_authentication_return_unauthorized(
        string method,
        string path)
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("malformed", 400)]
    [InlineData("W/\"00000001\"", 400)]
    public async Task Parse_requires_one_valid_strong_job_posting_etag(
        string? ifMatch,
        int expectedStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse",
            ifMatch);

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Parse_with_stale_job_posting_etag_returns_precondition_failed()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse",
            EntityTagCodec.Format(DifferentVersion(seeded.Posting.Version)));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Parse_for_another_users_posting_returns_not_found()
    {
        using var cancellation = CreateCancellation();
        var owner = CreateUser("owner");
        var caller = CreateUser("caller");
        var posting = CreatePosting(owner.Id);
        await SeedAsync(cancellation.Token, owner, caller, posting);
        using var client = CreateClient(caller);
        using var request = CreatePost(
            $"/api/job-postings/{posting.Id:D}/parse",
            EntityTagCodec.Format(posting.Version));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Parse_with_invalid_source_description_returns_bad_request()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token, description: "   ");
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse",
            EntityTagCodec.Format(seeded.Posting.Version));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task New_parse_returns_accepted_location_and_draft_etag_without_mutating_skills()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(
            cancellation.Token,
            includeJobPostingSkill: true);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse",
            EntityTagCodec.Format(seeded.Posting.Version));

        using var response = await client.SendAsync(request, cancellation.Token);
        var body = await response.Content
            .ReadFromJsonAsync<JobPostingParseDraftResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(JobPostingParseDraftStatus.Pending, body.Status);
        Assert.False(body.IsReused);
        Assert.Equal(EntityTagCodec.Format(body.DraftVersion), response.Headers.ETag?.Tag);
        Assert.Equal(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{body.DraftId:D}",
            response.Headers.Location?.AbsolutePath);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == body.DraftId, cancellation.Token);
        Assert.Equal(seeded.User.Id, persisted.UserId);
        Assert.Null(persisted.ProviderName);
        Assert.Null(persisted.ProviderRequestId);
        Assert.Equal(1, await verification.JobPostingSkills.CountAsync(
            cancellation.Token));
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Pending, 202)]
    [InlineData(JobPostingParseDraftStatus.Processing, 202)]
    [InlineData(JobPostingParseDraftStatus.Ready, 200)]
    public async Task Parse_reuses_active_draft_with_status_appropriate_response(
        JobPostingParseDraftStatus status,
        int expectedStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            status,
            cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse",
            EntityTagCodec.Format(seeded.Posting.Version));

        using var response = await client.SendAsync(request, cancellation.Token);
        var body = await response.Content
            .ReadFromJsonAsync<JobPostingParseDraftResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.IsReused);
        Assert.Equal(draft.Id, body.DraftId);
        Assert.Equal(status, body.Status);
        Assert.Equal(EntityTagCodec.Format(draft.Version), response.Headers.ETag?.Tag);
        Assert.Equal(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}",
            response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Detail_returns_owned_draft_etag_without_internal_source_or_lease_metadata()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Processing,
            cancellation.Token);
        using var client = CreateClient(seeded.User);

        using var response = await client.GetAsync(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}",
            cancellation.Token);
        var json = await response.Content.ReadAsStringAsync(cancellation.Token);
        var body = JsonSerializer.Deserialize<JobPostingParseDraftDetailResponse>(
            json,
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(draft.Id, body.DraftId);
        Assert.Equal(EntityTagCodec.Format(body.DraftVersion), response.Headers.ETag?.Tag);
        Assert.DoesNotContain("sourceDescription", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceTitle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaseOwner", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaseExpiresAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProvider", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Detail_with_foreign_owner_or_wrong_parent_route_returns_not_found(
        bool useForeignOwner)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Pending,
            cancellation.Token);
        var alternate = await SeedPostingAsync(cancellation.Token, userPrefix: "alternate");
        var caller = useForeignOwner ? alternate.User : seeded.User;
        var routePostingId = useForeignOwner
            ? seeded.Posting.Id
            : alternate.Posting.Id;
        using var client = CreateClient(caller);

        using var response = await client.GetAsync(
            $"/api/job-postings/{routePostingId:D}/parse-drafts/{draft.Id:D}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_supports_bounded_pagination_and_status_filter_without_graphs()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        await SeedDraftAsync(seeded.Posting, JobPostingParseDraftStatus.Failed,
            cancellation.Token, requestedAt: DateTimeOffset.UtcNow.AddMinutes(-3));
        var cancelled = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Cancelled,
            cancellation.Token,
            requestedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        await SeedDraftAsync(seeded.Posting, JobPostingParseDraftStatus.Failed,
            cancellation.Token, requestedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = CreateClient(seeded.User);

        using var pageResponse = await client.GetAsync(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts?page=1&pageSize=2",
            cancellation.Token);
        var page = await pageResponse.Content.ReadFromJsonAsync<
            PagedResponse<JobPostingParseDraftListItemResponse>>(ApiJson.Options, cancellation.Token);
        using var filterResponse = await client.GetAsync(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts?status=Cancelled",
            cancellation.Token);
        var filtered = await filterResponse.Content.ReadFromJsonAsync<
            PagedResponse<JobPostingParseDraftListItemResponse>>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        Assert.NotNull(filtered);
        Assert.Single(filtered.Items);
        Assert.Equal(cancelled.Id, filtered.Items[0].DraftId);
        Assert.Equal(JobPostingParseDraftStatus.Cancelled, filtered.Items[0].Status);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    public async Task History_rejects_invalid_query(string query)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        using var client = CreateClient(seeded.User);

        using var response = await client.GetAsync(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts?{query}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_for_another_users_posting_returns_not_found()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var foreign = await SeedPostingAsync(cancellation.Token, userPrefix: "foreign");
        using var client = CreateClient(foreign.User);

        using var response = await client.GetAsync(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("invalid-etag", 400)]
    public async Task Cancel_requires_valid_draft_etag(
        string? ifMatch,
        int expectedStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Pending,
            cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}/cancel",
            ifMatch);

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task Cancel_with_stale_draft_etag_returns_precondition_failed()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Pending,
            cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}/cancel",
            EntityTagCodec.Format(DifferentVersion(draft.Version)));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Pending)]
    [InlineData(JobPostingParseDraftStatus.Processing)]
    [InlineData(JobPostingParseDraftStatus.Ready)]
    public async Task Cancel_cancellable_draft_returns_cancelled_with_new_etag(
        JobPostingParseDraftStatus initialStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            initialStatus,
            cancellation.Token);
        var oldEtag = EntityTagCodec.Format(draft.Version);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}/cancel",
            oldEtag);

        using var response = await client.SendAsync(request, cancellation.Token);
        var body = await response.Content
            .ReadFromJsonAsync<JobPostingParseDraftResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(JobPostingParseDraftStatus.Cancelled, body.Status);
        Assert.NotEqual(draft.Version, body.DraftVersion);
        Assert.Equal(EntityTagCodec.Format(body.DraftVersion), response.Headers.ETag?.Tag);
        Assert.NotEqual(oldEtag, response.Headers.ETag?.Tag);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task Cancel_terminal_draft_returns_conflict(
        JobPostingParseDraftStatus status)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            status,
            cancellation.Token);
        using var client = CreateClient(seeded.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}/cancel",
            EntityTagCodec.Format(draft.Version));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_for_another_users_draft_returns_not_found()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync(cancellation.Token);
        var draft = await SeedDraftAsync(
            seeded.Posting,
            JobPostingParseDraftStatus.Pending,
            cancellation.Token);
        var foreign = await SeedPostingAsync(cancellation.Token, userPrefix: "foreign");
        using var client = CreateClient(foreign.User);
        using var request = CreatePost(
            $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{draft.Id:D}/cancel",
            EntityTagCodec.Format(draft.Version));

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(User user) =>
        new AuthenticatedHttpClientFactory(Application)
            .CreateForExistingUser(user);

    private async Task<SeededPosting> SeedPostingAsync(
        CancellationToken cancellationToken,
        string userPrefix = "owner",
        string description = Description,
        bool includeJobPostingSkill = false)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser(userPrefix);
        var posting = CreatePosting(user.Id, description);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellationToken);

        if (includeJobPostingSkill)
        {
            var skill = new Skill
            {
                Name = "C#",
                NormalizedName = "c#",
                Category = SkillCategory.ProgrammingLanguage
            };
            var requirement = new JobPostingSkill
            {
                JobPostingId = posting.Id,
                SkillId = skill.Id,
                RequirementType = JobSkillRequirementType.Required,
                ImportanceScore = 90
            };
            context.AddRange(skill, requirement);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SeededPosting(user, posting);
    }

    private async Task<JobPostingParseDraft> SeedDraftAsync(
        JobPosting posting,
        JobPostingParseDraftStatus status,
        CancellationToken cancellationToken,
        DateTimeOffset? requestedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var draft = new JobPostingParseDraft
        {
            JobPostingId = posting.Id,
            UserId = posting.UserId,
            SourceJobPostingVersion = posting.Version,
            SourceTitle = posting.Title,
            SourceDescription = posting.Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(
                posting.Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                posting.Title,
                posting.Description),
            ParserVersion = ParserVersion,
            Status = status,
            RequestedAt = requestedAt ?? now,
            ExpiresAt = now.AddHours(1)
        };

        if (status == JobPostingParseDraftStatus.Processing)
        {
            draft.AttemptCount = 1;
            draft.LeaseOwner = "http-test-worker";
            draft.LastAttemptAt = now.AddMinutes(-1);
            draft.LeaseExpiresAt = now.AddMinutes(4);
        }

        await using var context = Fixture.DbContextFactory.CreateDbContext();
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);
        return draft;
    }

    private async Task SeedAsync(
        CancellationToken cancellationToken,
        params object[] entities)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        context.AddRange(entities);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static User CreateUser(string prefix) => new()
    {
        FirstName = "HTTP",
        LastName = "Owner",
        Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreatePosting(
        Guid userId,
        string description = Description) => new()
        {
            UserId = userId,
            Title = "Senior Backend Engineer",
            Description = description,
            Status = JobPostingStatus.Draft
        };

    private static HttpRequestMessage CreatePost(string path, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeededPosting(User User, JobPosting Posting);
}
