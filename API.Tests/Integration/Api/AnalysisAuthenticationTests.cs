using System.Net;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class AnalysisAuthenticationTests : AnalysisApiTestBase
{
    public AnalysisAuthenticationTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GetAnalysis_WithoutToken_ReturnsUnauthorized()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();

        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_001):D}",
            cancellation.Token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task GetAnalysis_WithInvalidSignature_ReturnsUnauthorized()
    {
        using var cancellation = CreateCancellation();
        var now = DateTimeOffset.UtcNow;
        var invalidSecret = Convert.ToBase64String(
            Enumerable.Range(101, 32).Select(value => (byte)value).ToArray());
        var token = AuthenticatedHttpClientFactory.CreateControlledToken(
            Id(9_002),
            now.AddMinutes(-1),
            now.AddMinutes(5),
            invalidSecret);
        using var client = AuthenticatedClients.CreateWithBearerToken(token);

        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_003):D}",
            cancellation.Token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task GetAnalysis_WithTokenExpiredBeyondClockSkew_ReturnsUnauthorized()
    {
        using var cancellation = CreateCancellation();
        var now = DateTimeOffset.UtcNow;
        var token = AuthenticatedHttpClientFactory.CreateControlledToken(
            Id(9_004),
            now.AddMinutes(-10),
            now.AddMinutes(-2));
        using var client = AuthenticatedClients.CreateWithBearerToken(token);

        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_005):D}",
            cancellation.Token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task GetAnalysis_WithValidTokenForUnknownUser_ReturnsUnauthorized()
    {
        using var cancellation = CreateCancellation();
        var now = DateTimeOffset.UtcNow;
        var token = AuthenticatedHttpClientFactory.CreateControlledToken(
            Id(9_006),
            now.AddMinutes(-1),
            now.AddMinutes(5));
        using var client = AuthenticatedClients.CreateWithBearerToken(token);

        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_007):D}",
            cancellation.Token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task GetAnalysis_WithValidTokenForInactiveUser_ReturnsUnauthorized()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "inactive-api@example.com", isActive: false);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_008):D}",
            cancellation.Token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task GetOwnAnalysisHistory_WithProductionToken_ReturnsOk()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "active-api@example.com");
        var resume = CreateResume(10, user.Id);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(user, resume);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        using var client = AuthenticatedClients.CreateForExistingUser(user);
        using var response = await client.GetAsync(
            $"/api/resumes/{resume.Id:D}/analyses",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            value => string.Equals(
                value.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase));
    }
}
