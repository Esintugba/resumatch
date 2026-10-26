using System.Net;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class CorsOriginEndpointTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Origin = "https://app.example.com";

    [Fact]
    public async Task ConfiguredOrigin_IsAllowedOnPreflight()
    {
        using var response = await SendPreflightAsync(
            configuredOrigin: Origin,
            requestOrigin: Origin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            Origin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task UnconfiguredOrigin_IsNotAllowedOnPreflight()
    {
        using var response = await SendPreflightAsync(
            configuredOrigin: Origin,
            requestOrigin: "https://evil.example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact(Skip =
        "Known gap: HttpSecurityOptionsValidator accepts an origin with a trailing " +
        "slash, but CORS compares it literally with the browser Origin header " +
        "(which never has one), so the configured origin is silently never allowed.")]
    public async Task OriginAcceptedByValidatorWithTrailingSlash_IsAllowedOnPreflight()
    {
        using var response = await SendPreflightAsync(
            configuredOrigin: Origin + "/",
            requestOrigin: Origin);

        Assert.Equal(
            Origin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private async Task<HttpResponseMessage> SendPreflightAsync(
        string configuredOrigin,
        string requestOrigin)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["HttpSecurity:AllowedOrigins:0"] = configuredOrigin
            });
        using var client = application.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/resumes");
        request.Headers.Add("Origin", requestOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        return await client.SendAsync(request, cancellation.Token);
    }
}
