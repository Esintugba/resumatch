using System.Net;
using System.Net.Http.Json;
using Api.DTOs.Analysis;
using Api.DTOs.Resume;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class ResumeUnlistedSkillsNoteEndpointTests : AnalysisApiTestBase
{
    private const string Note = "Rust, COBOL ve şirket içi araçlar";

    public ResumeUnlistedSkillsNoteEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Note_IsSavedAsPlainTextAndReturnedByTheEditor()
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        const string markup = "<b>Rust</b> & \"COBOL\"";

        using var created = await client.PostAsJsonAsync(
            "/api/resumes",
            new { title = "Main", isMasterResume = true, unlistedSkillsNote = markup },
            cancellation.Token);
        var resume = await created.Content.ReadFromJsonAsync<ResumeResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(markup, resume!.UnlistedSkillsNote);
        var editor = await client.GetFromJsonAsync<ResumeEditorResponse>(
            $"/api/resumes/{resume.Id:D}/editor",
            ApiJson.Options,
            cancellation.Token);
        Assert.Equal(markup, editor!.UnlistedSkillsNote);
    }

    [Fact]
    public async Task Note_FollowsTheResumeConcurrencyToken()
    {
        using var cancellation = CreateCancellation();
        var (user, resume) = await SeedResumeAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var current = await client.GetFromJsonAsync<ResumeResponse>(
            $"/api/resumes/{resume.Id:D}",
            ApiJson.Options,
            cancellation.Token);

        using var missing = await PutNoteAsync(client, resume, null, cancellation.Token);
        using var updated = await PutNoteAsync(client, resume, current!.ETag, cancellation.Token);
        using var stale = await PutNoteAsync(client, resume, current.ETag, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(current.ETag, updated.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task Note_LongerThanTheLimit_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/resumes",
            new { title = "Main", isMasterResume = true, unlistedSkillsNote = new string('x', 501) },
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Note_DoesNotReachTheCatalogOrTheAnalysis()
    {
        using var cancellation = CreateCancellation();
        var (user, resume) = await SeedResumeAsync(cancellation.Token);
        var skill = CreateSkill(40, "Rust", "rust");
        var posting = CreateJobPosting(50, user.Id);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Add(skill);
            dbContext.Add(posting);
            dbContext.Add(new JobPostingSkill
            {
                Id = Id(60),
                JobPostingId = posting.Id,
                SkillId = skill.Id,
                RequirementType = JobSkillRequirementType.Required,
                ImportanceScore = 100,
                CreatedAt = SeedTime
            });
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var before = await AnalyzeAsync(client, resume, posting, cancellation.Token);
        var current = await client.GetFromJsonAsync<ResumeResponse>(
            $"/api/resumes/{resume.Id:D}",
            ApiJson.Options,
            cancellation.Token);
        using var _ = await PutNoteAsync(client, resume, current!.ETag, cancellation.Token);

        var after = await AnalyzeAsync(client, resume, posting, cancellation.Token);

        Assert.Equal(before.AtsScore, after.AtsScore);
        Assert.Equal(before.JobMatchScore, after.JobMatchScore);
        Assert.Equal(
            before.KeywordMatches.Select(item => (item.Keyword, item.Status)),
            after.KeywordMatches.Select(item => (item.Keyword, item.Status)));
        Assert.All(after.KeywordMatches, item =>
            Assert.NotEqual(KeywordMatchStatus.Matched, item.Status));
        await using var verification = CreateDbContext();
        Assert.Equal(1, await verification.Skills.CountAsync(cancellation.Token));
    }

    private static async Task<ResumeAnalysisResponse> AnalyzeAsync(
        HttpClient client,
        Resume resume,
        JobPosting posting,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new { resumeId = resume.Id, jobPostingId = posting.Id },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellationToken))!;
    }

    private static async Task<HttpResponseMessage> PutNoteAsync(
        HttpClient client,
        Resume resume,
        string? etag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/resumes/{resume.Id:D}")
        {
            Content = JsonContent.Create(new
            {
                title = resume.Title,
                professionalTitle = resume.ProfessionalTitle,
                unlistedSkillsNote = Note
            })
        };
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<User> SeedUserAsync(CancellationToken cancellationToken)
    {
        var user = CreateUser(1, "note@example.com");
        await using var dbContext = CreateDbContext();
        dbContext.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<(User User, Resume Resume)> SeedResumeAsync(
        CancellationToken cancellationToken)
    {
        var user = CreateUser(1, "note@example.com");
        var resume = CreateResume(10, user.Id);
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(user, resume);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (user, resume);
    }
}
