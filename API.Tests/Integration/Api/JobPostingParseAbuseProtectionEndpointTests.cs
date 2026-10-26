using System.Net;
using System.Net.Http.Json;
using Api.Application.Common.Concurrency;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests.Integration.Api;

public sealed class JobPostingParseAbuseProtectionEndpointTests :
    ApiIntegrationTestBase
{
    public JobPostingParseAbuseProtectionEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ParseRateLimit_IsPartitionedByAuthenticatedUser()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var first = await SeedAsync("first", cancellation.Token);
        var second = await SeedAsync("second", cancellation.Token);
        using var firstClient = CreateClient(first.User);
        using var secondClient = CreateClient(second.User);

        for (var index = 0; index < 10; index++)
        {
            using var response = await SendParseAsync(
                firstClient,
                first.Posting,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using var limited = await SendParseAsync(
            firstClient,
            first.Posting,
            cancellation.Token);
        using var independent = await SendParseAsync(
            secondClient,
            second.Posting,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json",
            limited.Content.Headers.ContentType?.MediaType);
        var problem = await limited.Content.ReadFromJsonAsync<ProblemDetails>(
            cancellation.Token);
        Assert.Equal(429, problem?.Status);
        Assert.Equal(HttpStatusCode.Accepted, independent.StatusCode);
    }

    [Fact]
    public async Task AnonymousParseRequests_RemainUnauthorizedInsteadOfConsumingUserQuota()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var client = Application.CreateHttpsClient();
        var path = $"/api/job-postings/{Guid.NewGuid():D}/parse";

        for (var index = 0; index < 12; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                EntityTagCodec.Format(1));
            using var response = await client.SendAsync(request, cancellation.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ReadCancelAndConfirmRoutes_DoNotConsumeParseQuota()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var seeded = await SeedAsync("routes", cancellation.Token);
        using var client = CreateClient(seeded.User);
        var unknownDraft = Guid.NewGuid();

        for (var index = 0; index < 5; index++)
        {
            using var detail = await client.GetAsync(
                $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{unknownDraft:D}",
                cancellation.Token);
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

            using var history = await client.GetAsync(
                $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts",
                cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);

            using var cancel = await client.PostAsync(
                $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{unknownDraft:D}/cancel",
                content: null,
                cancellation.Token);
            Assert.Equal((HttpStatusCode)428, cancel.StatusCode);

            using var confirm = await client.PostAsJsonAsync(
                $"/api/job-postings/{seeded.Posting.Id:D}/parse-drafts/{unknownDraft:D}/confirm",
                new { selectedSkills = Array.Empty<object>() },
                cancellation.Token);
            Assert.Equal((HttpStatusCode)428, confirm.StatusCode);
        }

        for (var index = 0; index < 10; index++)
        {
            using var response = await SendParseAsync(
                client,
                seeded.Posting,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using var limited = await SendParseAsync(
            client,
            seeded.Posting,
            cancellation.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RecentTerminalFingerprint_ReturnsSafeCooldownProblemDetails()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var seeded = await SeedAsync("cooldown", cancellation.Token);
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            context.JobPostingParseDrafts.Add(new JobPostingParseDraft
            {
                JobPostingId = seeded.Posting.Id,
                UserId = seeded.User.Id,
                SourceJobPostingVersion = seeded.Posting.Version,
                SourceTitle = seeded.Posting.Title,
                SourceDescription = seeded.Posting.Description,
                SourceDescriptionHash = DescriptionHashCalculator.Compute(
                    seeded.Posting.Description),
                SourceInputHash = SourceInputHashCalculator.Compute(
                    seeded.Posting.Title,
                    seeded.Posting.Description),
                ParserVersion = "job-parser-v1",
                Status = JobPostingParseDraftStatus.Cancelled,
                RequestedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            await context.SaveChangesAsync(cancellation.Token);
        }
        using var client = CreateClient(seeded.User);

        using var response = await SendParseAsync(
            client,
            seeded.Posting,
            cancellation.Token);
        var payload = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(
            seeded.Posting.Description,
            payload,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            seeded.User.Id.ToString("D"),
            payload,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SourceInputHash",
            payload,
            StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(User user) =>
        new AuthenticatedHttpClientFactory(Application)
            .CreateForExistingUser(user);

    private static async Task<HttpResponseMessage> SendParseAsync(
        HttpClient client,
        JobPosting posting,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{posting.Id:D}/parse");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(posting.Version));
        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<Seeded> SeedAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            FirstName = "Rate",
            LastName = "Limited",
            Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            Description = "C# and PostgreSQL are required.",
            Status = JobPostingStatus.Draft
        };
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellationToken);
        return new Seeded(user, posting);
    }

    private sealed record Seeded(User User, JobPosting Posting);
}
