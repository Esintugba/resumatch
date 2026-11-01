using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Application.Common.Concurrency;
using Api.DTOs.Analysis;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class JobPostingManualSkillEndpointTests : AnalysisApiTestBase
{
    public JobPostingManualSkillEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Add_CreatesAManualRowAndChangesTheETag()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);
        var etag = EntityTagCodec.Format(seed.Posting.Version);

        using var response = await AddAsync(client, seed.Posting.Id, seed.CSharp.Id, etag, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            ApiJson.Options,
            cancellation.Token);
        Assert.NotEqual(etag, body!.ETag);
        Assert.Equal(body.ETag, response.Headers.ETag?.Tag);
        var skill = Assert.Single(body.Skills);
        Assert.Equal(seed.CSharp.Id, skill.SkillId);
        Assert.Equal(JobSkillRequirementType.Required, skill.RequirementType);
        Assert.Equal(80, skill.ImportanceScore);
        Assert.Equal(JobPostingSkillProvenanceStatus.Manual, skill.ProvenanceStatus);
        Assert.False(skill.IsAiDetected);
        Assert.Null(skill.SourceParseDraftId);
    }

    [Fact]
    public async Task Add_RequiresACurrentETag()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);
        var etag = EntityTagCodec.Format(seed.Posting.Version);
        using var added = await AddAsync(client, seed.Posting.Id, seed.CSharp.Id, etag, cancellation.Token);

        using var missing = await AddAsync(client, seed.Posting.Id, seed.Docker.Id, null, cancellation.Token);
        using var stale = await AddAsync(client, seed.Posting.Id, seed.Docker.Id, etag, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(1, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Add_ToAnotherUsersPosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        var other = await SeedOtherUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(other);

        using var response = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        await AssertCodeAsync(response, HttpStatusCode.NotFound, "not_found", cancellation.Token);
        Assert.Equal(0, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Add_UnknownSkill_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);

        using var response = await AddAsync(
            client,
            seed.Posting.Id,
            Guid.NewGuid(),
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        await AssertCodeAsync(response, HttpStatusCode.NotFound, "not_found", cancellation.Token);
    }

    [Fact]
    public async Task Add_TheSameSkillTwice_ReturnsConflictAndKeepsTheRow()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);
        using var first = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        using var second = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            first.Headers.ETag!.Tag,
            cancellation.Token);

        await AssertCodeAsync(second, HttpStatusCode.Conflict, "duplicate_skill", cancellation.Token);
        Assert.Equal(1, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Add_PastTheLimit_ReturnsConflict()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        await using (var dbContext = CreateDbContext())
        {
            for (var index = 0; index < 50; index++)
            {
                var skill = CreateSkill(1000 + index, $"Skill {index}", $"skill {index}");
                dbContext.Add(skill);
                dbContext.Add(new JobPostingSkill
                {
                    JobPostingId = seed.Posting.Id,
                    SkillId = skill.Id,
                    RequirementType = JobSkillRequirementType.Preferred,
                    ImportanceScore = 10
                });
            }

            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);

        using var response = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            await CurrentETagAsync(seed.Posting.Id, cancellation.Token),
            cancellation.Token);

        await AssertCodeAsync(
            response,
            HttpStatusCode.Conflict,
            "job_posting_skill_limit_reached",
            cancellation.Token);
        Assert.Equal(50, await CountRowsAsync(cancellation.Token));
    }

    [Theory]
    [InlineData("""{"skillId":"SKILL","importanceScore":101}""")]
    [InlineData("""{"skillId":"SKILL","importanceScore":-1}""")]
    [InlineData("""{"skillId":"SKILL","requirementType":"Essential"}""")]
    [InlineData("""{"skillId":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task Add_InvalidBody_ReturnsValidationError(string body)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{seed.Posting.Id:D}/skills")
        {
            Content = new StringContent(
                body.Replace("SKILL", seed.CSharp.Id.ToString("D"), StringComparison.Ordinal),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTagCodec.Format(seed.Posting.Version));

        using var response = await client.SendAsync(request, cancellation.Token);

        await AssertCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            "validation_failed",
            cancellation.Token);
        Assert.Equal(0, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Remove_DeletesAManualRowAndChangesTheETag()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);
        using var added = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);
        var etag = added.Headers.ETag!.Tag;

        using var removed = await RemoveAsync(client, seed.Posting.Id, seed.CSharp.Id, etag, cancellation.Token);
        using var stale = await RemoveAsync(client, seed.Posting.Id, seed.CSharp.Id, etag, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.NotEqual(etag, removed.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(0, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Remove_RowThatIsNotOnThePosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);

        using var response = await RemoveAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);

        await AssertCodeAsync(response, HttpStatusCode.NotFound, "not_found", cancellation.Token);
    }

    [Fact]
    public async Task Remove_FromAnotherUsersPosting_ReturnsNotFoundAndKeepsTheRow()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var owner = AuthenticatedClients.CreateForExistingUser(seed.User);
        using var added = await AddAsync(
            owner,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);
        var other = await SeedOtherUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(other);

        using var response = await RemoveAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            added.Headers.ETag!.Tag,
            cancellation.Token);

        await AssertCodeAsync(response, HttpStatusCode.NotFound, "not_found", cancellation.Token);
        Assert.Equal(1, await CountRowsAsync(cancellation.Token));
    }

    [Fact]
    public async Task ManualSkills_DriveTheJobMatchScoreEndToEnd()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);

        var withoutSkills = await AnalyzeAsync(client, seed, cancellation.Token);
        Assert.Null(withoutSkills.JobMatchScore);

        using var csharp = await AddAsync(
            client,
            seed.Posting.Id,
            seed.CSharp.Id,
            EntityTagCodec.Format(seed.Posting.Version),
            cancellation.Token);
        using var docker = await AddAsync(
            client,
            seed.Posting.Id,
            seed.Docker.Id,
            csharp.Headers.ETag!.Tag,
            cancellation.Token);

        var partial = await AnalyzeAsync(client, seed, cancellation.Token);
        Assert.NotNull(partial.JobMatchScore);
        Assert.Contains(
            partial.KeywordMatches,
            item => item.SkillId == seed.CSharp.Id && item.Status == KeywordMatchStatus.Matched);
        Assert.Contains(
            partial.KeywordMatches,
            item => item.SkillId == seed.Docker.Id && item.Status == KeywordMatchStatus.Missing);

        using var removed = await RemoveAsync(
            client,
            seed.Posting.Id,
            seed.Docker.Id,
            docker.Headers.ETag!.Tag,
            cancellation.Token);

        var matched = await AnalyzeAsync(client, seed, cancellation.Token);
        Assert.True(matched.JobMatchScore > partial.JobMatchScore);
        Assert.DoesNotContain(matched.KeywordMatches, item => item.SkillId == seed.Docker.Id);
    }

    private static async Task<ResumeAnalysisResponse> AnalyzeAsync(
        HttpClient client,
        Seed seed,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new { resumeId = seed.Resume.Id, jobPostingId = seed.Posting.Id },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellationToken))!;
    }

    private static async Task<HttpResponseMessage> AddAsync(
        HttpClient client,
        Guid postingId,
        Guid skillId,
        string? etag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{postingId:D}/skills")
        {
            Content = JsonContent.Create(new
            {
                skillId,
                requirementType = "Required",
                importanceScore = 80
            })
        };
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> RemoveAsync(
        HttpClient client,
        Guid postingId,
        Guid skillId,
        string etag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/job-postings/{postingId:D}/skills/{skillId:D}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task AssertCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private async Task<int> CountRowsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.JobPostingSkills.CountAsync(cancellationToken);
    }

    private async Task<string> CurrentETagAsync(
        Guid postingId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        var posting = await dbContext.JobPostings.AsNoTracking()
            .SingleAsync(item => item.Id == postingId, cancellationToken);
        return EntityTagCodec.Format(posting.Version);
    }

    private async Task<User> SeedOtherUserAsync(CancellationToken cancellationToken)
    {
        var other = CreateUser(2, "other@example.com");
        await using var dbContext = CreateDbContext();
        dbContext.Add(other);
        await dbContext.SaveChangesAsync(cancellationToken);
        return other;
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        var user = CreateUser(1, "manual-skill@example.com");
        var resume = CreateResume(10, user.Id);
        var posting = CreateJobPosting(20, user.Id);
        var csharp = CreateSkill(30, "C#", "c#");
        var docker = CreateSkill(31, "Docker", "docker");
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(user, resume, posting, csharp, docker);
        dbContext.Add(new ResumeSkill
        {
            Id = Id(32),
            ResumeId = resume.Id,
            SkillId = csharp.Id,
            SortOrder = 0,
            CreatedAt = SeedTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new Seed(user, resume, posting, csharp, docker);
    }

    private sealed record Seed(
        User User,
        Resume Resume,
        JobPosting Posting,
        Skill CSharp,
        Skill Docker);
}
