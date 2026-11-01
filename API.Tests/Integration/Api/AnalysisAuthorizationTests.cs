using System.Net;
using System.Net.Http.Json;
using Api.DTOs.Analysis;
using Api.Entities;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class AnalysisAuthorizationTests : AnalysisApiTestBase
{
    public AnalysisAuthorizationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Analyze_WhenResumeBelongsToAnotherUser_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var data = await SeedOwnershipGraphAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(data.UserA);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = data.ResumeB.Id },
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Analyze_WhenJobPostingBelongsToAnotherUser_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var data = await SeedOwnershipGraphAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(data.UserA);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = data.ResumeA.Id,
                JobPostingId = data.JobPostingB.Id
            },
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalysis_WhenOwnedByAnotherUser_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var data = await SeedOwnershipGraphAsync(cancellation.Token);
        var analysisB = CreateAnalysis(
            100,
            data.UserB.Id,
            data.ResumeB.Id,
            SeedTime);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ResumeAnalyses.Add(analysisB);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }
        using var client = AuthenticatedClients.CreateForExistingUser(data.UserA);

        using var response = await client.GetAsync(
            $"/api/analyses/{analysisB.Id:D}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_WhenResumeBelongsToAnotherUser_ReturnsNotFoundWithoutData()
    {
        using var cancellation = CreateCancellation();
        var data = await SeedOwnershipGraphAsync(cancellation.Token);
        var analysisB = CreateAnalysis(
            101,
            data.UserB.Id,
            data.ResumeB.Id,
            SeedTime);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ResumeAnalyses.Add(analysisB);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }
        using var client = AuthenticatedClients.CreateForExistingUser(data.UserA);

        using var response = await client.GetAsync(
            $"/api/resumes/{data.ResumeB.Id:D}/analyses",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellation.Token);
        Assert.DoesNotContain(analysisB.Id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(data.ResumeB.Title, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAnalysis_WhenOwnedByCurrentUser_ReturnsOk()
    {
        using var cancellation = CreateCancellation();
        var data = await SeedOwnershipGraphAsync(cancellation.Token);
        var analysisA = CreateAnalysis(
            102,
            data.UserA.Id,
            data.ResumeA.Id,
            SeedTime);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ResumeAnalyses.Add(analysisA);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }
        using var client = AuthenticatedClients.CreateForExistingUser(data.UserA);

        using var response = await client.GetAsync(
            $"/api/analyses/{analysisA.Id:D}",
            cancellation.Token);
        var body = await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(analysisA.Id, body.Id);
        await using var verificationContext = CreateDbContext();
        Assert.True(await verificationContext.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(
                analysis =>
                    analysis.Id == body.Id &&
                    analysis.UserId == data.UserA.Id,
                cancellation.Token));
    }

    private async Task<OwnershipGraph> SeedOwnershipGraphAsync(
        CancellationToken cancellationToken)
    {
        var userA = CreateUser(1, "owner-a@example.com");
        var userB = CreateUser(2, "owner-b@example.com");
        var resumeA = CreateResume(10, userA.Id, "User A Resume");
        var resumeB = CreateResume(20, userB.Id, "User B Private Resume");
        var jobPostingA = CreateJobPosting(30, userA.Id, "User A Job");
        var jobPostingB = CreateJobPosting(40, userB.Id, "User B Private Job");

        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            userA,
            userB,
            resumeA,
            resumeB,
            jobPostingA,
            jobPostingB);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new OwnershipGraph(
            userA,
            userB,
            resumeA,
            resumeB,
            jobPostingA,
            jobPostingB);
    }

    private sealed record OwnershipGraph(
        User UserA,
        User UserB,
        Resume ResumeA,
        Resume ResumeB,
        JobPosting JobPostingA,
        JobPosting JobPostingB);
}
