using System.Net;
using System.Net.Http.Json;
using Api.DTOs.Skill;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using API.Tests.Skills;

namespace API.Tests.Integration.Api;

public sealed class SkillSearchEndpointTests : AnalysisApiTestBase
{
    public SkillSearchEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData("iletişim", "Communication")]
    [InlineData("İLETİŞİM", "Communication")]
    [InlineData("iletisim", "Communication")]
    [InlineData("ILETISIM", "Communication")]
    [InlineData("Ölçeklenebilirlik", "Scalability")]
    [InlineData("ÖLÇEKLENEBİLİRLİK", "Scalability")]
    [InlineData("olceklenebilirlik", "Scalability")]
    [InlineData("çatışma çözme", "Conflict Resolution")]
    [InlineData("catisma cozme", "Conflict Resolution")]
    [InlineData("ağ yönetimi", "Network Administration")]
    [InlineData("ag yonetimi", "Network Administration")]
    [InlineData("ÜRÜN YÖNETİMİ", "Product Management")]
    [InlineData("duygusal zeka", "Emotional Intelligence")]
    [InlineData("duygusal zekâ", "Emotional Intelligence")]
    [InlineData("Communication", "Communication")]
    public async Task Turkish_FindsTheSkillInAnySpelling(string search, string expected)
    {
        var names = await SearchAsync(search);

        Assert.Equal(expected, names[0]);
    }

    [Theory]
    [InlineData("c#", "C#")]
    [InlineData("C#", "C#")]
    [InlineData("c++", "C++")]
    [InlineData(".net", ".NET")]
    [InlineData(".NET", ".NET")]
    [InlineData("f#", "F#")]
    [InlineData("ci/cd", "CI/CD")]
    [InlineData("go", "Go")]
    [InlineData("r", "R")]
    [InlineData("c", "C")]
    [InlineData("java", "Java")]
    [InlineData("react", "React")]
    public async Task ExactMatch_ComesFirst(string search, string expected)
    {
        var names = await SearchAsync(search);

        Assert.Equal(expected, names[0]);
    }

    [Fact]
    public async Task PrefixMatches_ComeBeforeOtherMatches()
    {
        var names = await SearchAsync("react");

        Assert.Equal(["React", "React Native"], names.Take(2).ToArray());
        Assert.Contains("ASP.NET Core", await SearchAsync(".net"));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task SqlWildcards_AreLiteral(string search)
    {
        Assert.Empty(await SearchAsync(search));
    }

    [Theory]
    [InlineData("postgres", "PostgreSQL")]
    [InlineData("node", "Node.js")]
    [InlineData("mongo", "MongoDB")]
    public async Task PartialNames_FindTheCanonicalRecord(string search, string expected)
    {
        Assert.Contains(expected, await SearchAsync(search));
    }

    [Theory]
    [InlineData("js", "JavaScript")]
    [InlineData("ts", "TypeScript")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("golang", "Go")]
    public async Task Abbreviations_AreNotResolvedYet(string search, string canonicalName)
    {
        Assert.DoesNotContain(canonicalName, await SearchAsync(search));
    }

    private async Task<string[]> SearchAsync(string search)
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "skills@example.com");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Users.Add(user);
            dbContext.Skills.AddRange(SkillCatalogFile.Read().Select(row => row.ToSkill()));
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        using var response = await client.GetAsync(
            $"/api/skills?pageSize=100&search={Uri.EscapeDataString(search)}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var skills = await response.Content.ReadFromJsonAsync<IReadOnlyList<SkillResponse>>(
            ApiJson.Options,
            cancellation.Token);
        await Fixture.ResetDatabaseAsync(cancellation.Token);
        return skills!.Select(skill => skill.Name).ToArray();
    }
}
