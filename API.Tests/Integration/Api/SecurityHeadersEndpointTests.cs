using System.Net;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class SecurityHeadersEndpointTests : AnalysisApiTestBase
{
    public SecurityHeadersEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SuccessfulResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/live", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AuthenticatedSuccessfulResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = await AuthenticatedClients.CreateAsync(
            CreateUser(1, "headers-ok@example.com"),
            cancellation.Token);

        using var response = await client.GetAsync("/api/resumes", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task UnauthorizedResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();

        using var response = await client.GetAsync("/api/resumes", cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task NotFoundResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = await AuthenticatedClients.CreateAsync(
            CreateUser(2, "headers-404@example.com"),
            cancellation.Token);

        using var response = await client.GetAsync(
            $"/api/resumes/{Guid.NewGuid():D}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task UnmatchedRouteResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = await AuthenticatedClients.CreateAsync(
            CreateUser(3, "headers-route@example.com"),
            cancellation.Token);

        using var response = await client.GetAsync(
            "/api/does-not-exist",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ValidationProblemResponse_IncludesAllSecurityHeaders()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new StringContent(
            "{}",
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task SecurityHeaders_AreNotDuplicated()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/live", cancellation.Token);

        foreach (var name in ExpectedHeaders.Keys)
        {
            Assert.Single(GetHeaderValues(response, name));
        }
    }

    internal static readonly IReadOnlyDictionary<string, string> ExpectedHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["Referrer-Policy"] = "no-referrer",
            ["X-Frame-Options"] = "DENY",
            ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()"
        };

    internal static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        foreach (var (name, expected) in ExpectedHeaders)
        {
            var values = GetHeaderValues(response, name);
            Assert.True(values.Length > 0, $"Missing security header '{name}'.");
            Assert.Equal(expected, string.Join(",", values));
        }

        Assert.False(
            response.Headers.Contains("Server"),
            "The Server header must not be exposed.");
    }

    private static string[] GetHeaderValues(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) ||
            response.Content.Headers.TryGetValues(name, out values))
        {
            return values.ToArray();
        }

        return [];
    }
}
