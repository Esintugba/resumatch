using System.Net;
using System.Net.Http.Json;
using Api.DTOs.Analysis;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

/// <summary>Search aliases must stay out of analysis, matching and responses.</summary>
public sealed class SkillAliasIsolationTests : AnalysisApiTestBase
{
    public SkillAliasIsolationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Analysis_GivesTheSameResultWithAndWithoutAliases()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "alias@example.com");
        var resume = CreateResume(10, user.Id);
        var posting = CreateJobPosting(20, user.Id);
        var csharp = CreateSkill(30, "C#", "c#");
        var docker = CreateSkill(31, "Docker", "docker");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(user, resume, posting, csharp, docker);
            dbContext.Add(new ResumeSkill { Id = Id(32), ResumeId = resume.Id, SkillId = csharp.Id, CreatedAt = SeedTime });
            dbContext.Add(Requirement(33, posting, csharp));
            dbContext.Add(Requirement(34, posting, docker));
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var before = await AnalyzeAsync(client, resume, posting, cancellation.Token);
        await using (var dbContext = CreateDbContext())
        {
            // Aliases that would match the other skill if aliases took part in matching.
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Skills" SET "SearchAliases" = ARRAY['docker', 'csharp', 'c#']
                WHERE "NormalizedName" IN ('c#', 'docker');
                """,
                cancellation.Token);
        }

        var after = await AnalyzeAsync(client, resume, posting, cancellation.Token);

        Assert.Equal(before.AtsScore, after.AtsScore);
        Assert.Equal(before.JobMatchScore, after.JobMatchScore);
        Assert.Equal(before.KeywordScore, after.KeywordScore);
        Assert.Equal(
            before.KeywordMatches.Select(item => (item.SkillId, item.Status)),
            after.KeywordMatches.Select(item => (item.SkillId, item.Status)));
        Assert.Contains(after.KeywordMatches, item =>
            item.SkillId == docker.Id && item.Status == KeywordMatchStatus.Missing);
    }

    [Fact]
    public async Task Aliases_AreNotPartOfAnyResponse()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "alias@example.com");
        var posting = CreateJobPosting(20, user.Id);
        var skill = CreateSkill(30, "Kubernetes", "kubernetes");
        skill.SearchAliases = ["k8s", "kube"];
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(user, posting, skill);
            dbContext.Add(Requirement(33, posting, skill));
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var bodies = new List<string>();
        foreach (var path in new[]
        {
            "/api/skills?search=k8s",
            "/api/skills",
            $"/api/job-postings/{posting.Id:D}"
        })
        {
            using var response = await client.GetAsync(path, cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync(cancellation.Token));
        }

        Assert.Contains("Kubernetes", bodies[0], StringComparison.Ordinal);
        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain("alias", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("k8s", body, StringComparison.Ordinal);
            Assert.DoesNotContain("kube\"", body, StringComparison.Ordinal);
        });
    }

    private static JobPostingSkill Requirement(int id, JobPosting posting, Skill skill) => new()
    {
        Id = Id(id),
        JobPostingId = posting.Id,
        SkillId = skill.Id,
        RequirementType = JobSkillRequirementType.Required,
        ImportanceScore = 100,
        CreatedAt = SeedTime
    };

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
}
