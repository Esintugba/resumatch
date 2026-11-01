using System.Net;
using System.Net.Http.Json;
using Api.DTOs.Analysis;
using Api.DTOs.Common;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class AnalysisEndpointTests : AnalysisApiTestBase
{
    public AnalysisEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PostAnalysis_AtsOnly_ReturnsCreatedAndPersistsResult()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "ats-only@example.com");
        var resume = CreateResume(10, user.Id);
        await SeedAsync(cancellation.Token, user, resume);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);
        var body = await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(body);
        Assert.Equal(resume.Id, body.ResumeId);
        Assert.Equal(AnalysisType.AtsOnly, body.Type);
        Assert.Null(body.JobPostingId);
        Assert.Null(body.JobMatchScore);
        Assert.Equal("ats-v1", body.AnalyzerVersion);
        Assert.InRange(body.AtsScore, 0m, 100m);

        await using var verificationContext = CreateDbContext();
        var persisted = await verificationContext.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(
                analysis => analysis.Id == body.Id,
                cancellation.Token);
        Assert.Equal(user.Id, persisted.UserId);
        Assert.Equal(body.AtsScore, persisted.AtsScore);
    }

    [Fact]
    public async Task PostAnalysis_JobMatch_PersistsMatchedAndMissingKeywords()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "job-match@example.com");
        var resume = CreateResume(10, user.Id);
        var jobPosting = CreateJobPosting(20, user.Id);
        var matchedSkill = CreateSkill(30, "C#", "c#");
        var missingSkill = CreateSkill(31, "Docker", "docker");
        var resumeSkill = new ResumeSkill
        {
            Id = Id(32),
            ResumeId = resume.Id,
            SkillId = matchedSkill.Id,
            SortOrder = 0,
            CreatedAt = SeedTime
        };
        var matchedRequirement = new JobPostingSkill
        {
            Id = Id(33),
            JobPostingId = jobPosting.Id,
            SkillId = matchedSkill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 100,
            CreatedAt = SeedTime
        };
        var missingRequirement = new JobPostingSkill
        {
            Id = Id(34),
            JobPostingId = jobPosting.Id,
            SkillId = missingSkill.Id,
            RequirementType = JobSkillRequirementType.Preferred,
            ImportanceScore = 100,
            CreatedAt = SeedTime
        };
        await SeedAsync(
            cancellation.Token,
            user,
            resume,
            jobPosting,
            matchedSkill,
            missingSkill,
            resumeSkill,
            matchedRequirement,
            missingRequirement);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = jobPosting.Id
            },
            cancellation.Token);
        var body = await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(AnalysisType.JobMatch, body.Type);
        Assert.NotNull(body.JobMatchScore);
        Assert.Contains(
            body.KeywordMatches,
            match =>
                match.SkillId == matchedSkill.Id &&
                match.Status == KeywordMatchStatus.Matched);
        Assert.Contains(
            body.KeywordMatches,
            match =>
                match.SkillId == missingSkill.Id &&
                match.Status == KeywordMatchStatus.Missing);
        Assert.Equal(100.00m, body.KeywordMatches.Sum(match => match.Weight));
        Assert.Equal(
            body.KeywordMatches
                .Where(match => match.Status == KeywordMatchStatus.Matched)
                .Sum(match => match.Weight),
            body.JobMatchScore);

        await using var verificationContext = CreateDbContext();
        var persisted = await verificationContext.ResumeAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.KeywordMatches)
            .SingleAsync(
                analysis => analysis.Id == body.Id,
                cancellation.Token);
        Assert.Equal(body.KeywordMatches.Count, persisted.KeywordMatches.Count);
        Assert.Equal(100.00m, persisted.KeywordMatches.Sum(match => match.Weight));
    }

    [Fact]
    public async Task PostAnalysis_JobWithoutRequirements_ReturnsNullScoreAndInputIssue()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "no-requirements@example.com");
        var resume = CreateResume(10, user.Id);
        var jobPosting = CreateJobPosting(20, user.Id);
        await SeedAsync(cancellation.Token, user, resume, jobPosting);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = resume.Id,
                JobPostingId = jobPosting.Id
            },
            cancellation.Token);
        var body = await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Null(body.JobMatchScore);
        Assert.Contains(body.Issues, issue => issue.Code == "JOB.INP.001");
    }

    [Fact]
    public async Task GetAnalysis_ReturnsPersistedScoresAndCompleteResponseCollections()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "get-analysis@example.com");
        var resume = CreateResume(10, user.Id);
        await SeedAsync(cancellation.Token, user, resume);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);
        var created = await createResponse.Content
            .ReadFromJsonAsync<ResumeAnalysisResponse>(ApiJson.Options, cancellation.Token);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        using var getResponse = await client.GetAsync(
            $"/api/analyses/{created.Id:D}",
            cancellation.Token);
        var fetched = await getResponse.Content
            .ReadFromJsonAsync<ResumeAnalysisResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.ResumeId, fetched.ResumeId);
        Assert.Equal(created.AtsScore, fetched.AtsScore);
        Assert.Equal(created.StructureScore, fetched.StructureScore);
        Assert.Equal(created.FormattingScore, fetched.FormattingScore);
        Assert.Equal(created.ContentScore, fetched.ContentScore);
        Assert.Equal(created.KeywordScore, fetched.KeywordScore);
        Assert.Equal(created.Issues.Count, fetched.Issues.Count);
        Assert.Equal(created.KeywordMatches.Count, fetched.KeywordMatches.Count);
        Assert.Equal(created.Recommendations.Count, fetched.Recommendations.Count);

        await using var verificationContext = CreateDbContext();
        var persisted = await verificationContext.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(
                analysis => analysis.Id == fetched.Id,
                cancellation.Token);
        Assert.Equal(user.Id, persisted.UserId);
        Assert.Equal(fetched.AtsScore, persisted.AtsScore);
    }

    [Fact]
    public async Task PostThenGetAnalysis_PreservesRecommendationMetadataAndOrder()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "metadata-http@example.com");
        var resume = CreateResume(10, user.Id, "Incomplete Metadata Resume");
        resume.ProfessionalTitle = null;
        await SeedAsync(cancellation.Token, user, resume);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var postResponse = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);
        var created = await postResponse.Content
            .ReadFromJsonAsync<ResumeAnalysisResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("ats-v1", created.AnalyzerVersion);
        Assert.True(created.Recommendations.Count > 1);
        AssertRecommendationMetadata(created.Recommendations);

        await using (var verificationContext = CreateDbContext())
        {
            var persisted = await verificationContext.AnalysisRecommendations
                .AsNoTracking()
                .Where(item => item.ResumeAnalysisId == created.Id)
                .OrderBy(item => item.SortOrder)
                .Select(item => new { item.Id, item.RuleCode, item.SortOrder })
                .ToListAsync(cancellation.Token);

            Assert.Equal(
                created.Recommendations.Select(item => item.Id),
                persisted.Select(item => item.Id));
            Assert.Equal(
                created.Recommendations.Select(item => item.RuleCode),
                persisted.Select(item => item.RuleCode));
            Assert.Equal(
                created.Recommendations.Select(item => item.SortOrder),
                persisted.Select(item => item.SortOrder));
        }

        using var getResponse = await client.GetAsync(
            $"/api/analyses/{created.Id:D}",
            cancellation.Token);
        var fetched = await getResponse.Content
            .ReadFromJsonAsync<ResumeAnalysisResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(fetched);
        Assert.Equal(created.AnalyzerVersion, fetched.AnalyzerVersion);
        Assert.Equal(
            created.Recommendations.Select(item => item.Id),
            fetched.Recommendations.Select(item => item.Id));
        Assert.Equal(
            created.Recommendations.Select(item => item.RuleCode),
            fetched.Recommendations.Select(item => item.RuleCode));
        Assert.Equal(
            created.Recommendations.Select(item => item.SortOrder),
            fetched.Recommendations.Select(item => item.SortOrder));
        AssertRecommendationMetadata(fetched.Recommendations);
    }

    [Fact]
    public async Task GetAnalysisHistory_ReturnsOwnedDeterministicallyOrderedPage()
    {
        using var cancellation = CreateCancellation();
        var userA = CreateUser(1, "history-a@example.com");
        var userB = CreateUser(2, "history-b@example.com");
        var resumeA = CreateResume(10, userA.Id);
        var resumeB = CreateResume(20, userB.Id);
        var oldest = CreateAnalysis(101, userA.Id, resumeA.Id, SeedTime);
        var middle = CreateAnalysis(102, userA.Id, resumeA.Id, SeedTime.AddMinutes(1));
        var newest = CreateAnalysis(103, userA.Id, resumeA.Id, SeedTime.AddMinutes(2));
        var foreign = CreateAnalysis(104, userB.Id, resumeB.Id, SeedTime.AddMinutes(3));
        await SeedAsync(
            cancellation.Token,
            userA,
            userB,
            resumeA,
            resumeB,
            oldest,
            middle,
            newest,
            foreign);
        using var client = AuthenticatedClients.CreateForExistingUser(userA);

        using var response = await client.GetAsync(
            $"/api/resumes/{resumeA.Id:D}/analyses?page=1&pageSize=2",
            cancellation.Token);
        var body = await response.Content
            .ReadFromJsonAsync<PagedResponse<AnalysisListItemResponse>>(
                ApiJson.Options,
                cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(1, body.Page);
        Assert.Equal(2, body.PageSize);
        Assert.Equal(3, body.TotalCount);
        Assert.Equal(2, body.TotalPages);
        Assert.Equal([newest.Id, middle.Id], body.Items.Select(item => item.Id));
        Assert.DoesNotContain(body.Items, item => item.Id == foreign.Id);
        Assert.All(body.Items, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.AnalyzerVersion)));
    }

    private async Task SeedAsync(
        CancellationToken cancellationToken,
        params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AssertRecommendationMetadata(
        IReadOnlyList<AnalysisRecommendationResponse> recommendations)
    {
        Assert.Equal(
            Enumerable.Range(0, recommendations.Count).Select(value => (int?)value),
            recommendations.Select(item => item.SortOrder));
        Assert.Equal(
            recommendations.OrderBy(item => item.SortOrder).Select(item => item.Id),
            recommendations.Select(item => item.Id));
        Assert.All(recommendations, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.RuleCode)));
    }
}
