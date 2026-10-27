using System.Net;
using System.Net.Http.Headers;
using Api.Application.Common.Interfaces;
using Api.Entities;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api;

public sealed class OpenApiExposureEndpointTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string OpenApiPath = "/openapi/v1.json";
    private const string SwaggerUiPath = "/swagger/index.html";

    [Fact]
    public async Task Development_OpenApiDocument_IsServedAnonymously()
    {
        using var cancellation = CreateCancellation();
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            environmentName: "Development");
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync(OpenApiPath, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync(cancellation.Token);
        Assert.Contains("\"openapi\"", document, StringComparison.Ordinal);
        Assert.Contains("/api/auth/login", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_SwaggerUi_IsServedAnonymouslyAndLoadsOpenApiDocument()
    {
        using var cancellation = CreateCancellation();
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            environmentName: "Development");
        using var client = application.CreateHttpsClient();

        using var page = await client.GetAsync(SwaggerUiPath, cancellation.Token);
        using var script = await client.GetAsync("/swagger/index.js", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var configuration =
            await page.Content.ReadAsStringAsync(cancellation.Token) +
            (script.IsSuccessStatusCode
                ? await script.Content.ReadAsStringAsync(cancellation.Token)
                : string.Empty);
        Assert.Contains(OpenApiPath, configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("/swagger/v1/swagger.json", configuration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenApiPath)]
    [InlineData(SwaggerUiPath)]
    public async Task Production_OpenApiAndSwaggerUi_AreNotMapped(string path)
    {
        using var cancellation = CreateCancellation();
        using var application = ProductionApplication.Create(Fixture);
        using var anonymous = ProductionApplication.CreateClient(application);
        using var authenticated = await CreateAuthenticatedClientAsync(
            application,
            cancellation.Token);

        using var anonymousResponse = await anonymous.GetAsync(path, cancellation.Token);
        using var authenticatedResponse = await authenticated.GetAsync(
            path,
            cancellation.Token);

        // The fallback policy answers 401 for every unmatched route when the
        // caller is anonymous; with a valid token the route is plainly absent.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, authenticatedResponse.StatusCode);
    }

    [Fact]
    public void Production_WithExposeOpenApi_RefusesToStart()
    {
        using var application = ProductionApplication.Create(
            Fixture,
            new Dictionary<string, string?>
            {
                ["HttpSecurity:ExposeOpenApi"] = "true"
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _ = application.Services);
        Assert.Contains(
            "The OpenAPI document and Swagger UI cannot be exposed in production.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_HttpsResponse_IncludesHsts()
    {
        using var cancellation = CreateCancellation();
        using var application = ProductionApplication.Create(Fixture);
        using var client = ProductionApplication.CreateClient(application);

        using var response = await client.GetAsync("/health/live", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "max-age=2592000",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(
        ResuMatchWebApplicationFactory application,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "OpenApi",
            LastName = "Exposure",
            Email = "openapi-exposure@example.com",
            PasswordHash = "integration-test-password-hash",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
        }

        var token = application.Services
            .GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(user)
            .Token;
        var client = ProductionApplication.CreateClient(application);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(30));
}
