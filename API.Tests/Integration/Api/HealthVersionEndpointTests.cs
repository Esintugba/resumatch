using System.Net;
using System.Text.Json;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class HealthVersionEndpointTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task Version_IsAnonymousAndReturnsOnlyTheCommit()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?> { ["RENDER_GIT_COMMIT"] = Commit });
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/version", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellation.Token));
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("commit", property.Name);
        Assert.Equal(Commit, property.Value.GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Version_WithoutCommit_ReturnsUnknown(string? commit)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?> { ["RENDER_GIT_COMMIT"] = commit });
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/version", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellation.Token));
        Assert.Equal("unknown", json.RootElement.GetProperty("commit").GetString());
    }

    [Fact]
    public async Task Version_DoesNotContactDatabase()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Command Timeout=1",
                ["RENDER_GIT_COMMIT"] = Commit
            });
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/version", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
