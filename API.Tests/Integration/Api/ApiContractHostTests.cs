using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Application.Common.Exceptions;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

/// <summary>
/// Contract checks that need their own host configuration. A test may start
/// only one host, because each one resets the shared database.
/// </summary>
public sealed class ApiContractHostTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Origin = "https://app.example.com";

    [Fact]
    public async Task OpenApiDocument_DescribesEnumsAsStrings()
    {
        using var cancellation = CreateCancellation();
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            environmentName: "Development");
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/openapi/v1.json", cancellation.Token);
        using var document = await ReadJsonAsync(response, cancellation.Token);

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var schema = schemas.GetProperty(nameof(ApplicationStatus));
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            Enum.GetNames<ApplicationStatus>(),
            schema.GetProperty("enum").EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.True(schemas
            .GetProperty("ProblemDetails")
            .GetProperty("properties")
            .TryGetProperty("code", out _));
    }

    [Fact]
    public async Task AllowedOrigin_CanReadRetryAfterOnRateLimit()
    {
        using var cancellation = CreateCancellation();
        using var application = CreateRateLimitedApplication();
        using var client = application.CreateHttpsClient();
        using var _ = await LoginAsync(client, Origin, cancellation.Token);

        using var limited = await LoginAsync(client, Origin, cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        AssertExposedHeaders(limited);
        using var body = await ReadJsonAsync(limited, cancellation.Token);
        Assert.Equal(
            ApiErrorCodes.RateLimited,
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AllowedOrigin_CanReadETag()
    {
        using var cancellation = CreateCancellation();
        using var application = CreateRateLimitedApplication();
        var clients = new AuthenticatedHttpClientFactory(application);
        var user = new User
        {
            FirstName = "Contract",
            LastName = "Test",
            Email = "contract@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        using var client = await clients.CreateAsync(user, cancellation.Token);
        client.DefaultRequestHeaders.Add("Origin", Origin);

        using var response = await client.PostAsync(
            "/api/job-postings",
            new StringContent(
                """{"title":"Engineer","description":"C# is required."}""",
                Encoding.UTF8,
                "application/json"),
            cancellation.Token);

        Assert.NotNull(response.Headers.ETag);
        AssertExposedHeaders(response);
    }

    [Fact]
    public async Task OtherOrigin_ReceivesNoExposedHeaders()
    {
        using var cancellation = CreateCancellation();
        using var application = CreateRateLimitedApplication();
        using var client = application.CreateHttpsClient();
        using var _ = await LoginAsync(client, "https://evil.example.com", cancellation.Token);

        using var limited = await LoginAsync(
            client,
            "https://evil.example.com",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.False(limited.Headers.Contains("Access-Control-Expose-Headers"));
        Assert.False(limited.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private ResuMatchWebApplicationFactory CreateRateLimitedApplication() =>
        new(
            Fixture,
            new Dictionary<string, string?>
            {
                ["HttpSecurity:AllowedOrigins:0"] = Origin,
                ["AuthenticationRateLimits:LoginPermitLimit"] = "1",
                ["AuthenticationRateLimits:LoginWindow"] = "01:00:00"
            });

    private static void AssertExposedHeaders(HttpResponseMessage response)
    {
        Assert.Equal(
            Origin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var exposed = string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("ETag", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry-After", exposed, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string origin,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "nobody@example.com",
                password = "not-a-real-password"
            })
        };
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request, cancellationToken);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
}
