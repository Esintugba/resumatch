using System.Net;
using System.Text.Json;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class HealthEndpointTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HealthEndpoints_AreAnonymousAndPostgreSqlReady()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(Fixture);
        using var client = application.CreateHttpsClient();

        using var live = await client.GetAsync("/health/live", cancellation.Token);
        using var ready = await client.GetAsync("/health/ready", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var readyJson = await ready.Content.ReadAsStringAsync(cancellation.Token);
        Assert.Contains("database", readyJson, StringComparison.Ordinal);
        Assert.Contains("configuration", readyJson, StringComparison.Ordinal);
        Assert.Contains("job-parser", readyJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_DoesNotContactDatabaseOrProvider()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var handler = new CountingHandler();
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Command Timeout=1"
            },
            handler);
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/live", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Readiness_WhenDatabaseUnavailable_IsSanitizedAndUnhealthy()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        const string secret = "do-not-leak-db-password";
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password={secret};Timeout=1;Command Timeout=1",
                ["Observability:DatabaseCheckTimeout"] = "00:00:00.500"
            });
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/ready", cancellation.Token);
        var payload = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(secret, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", payload, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OpenCircuit_DoesNotFailLivenessOrReadiness()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            context.AiProviderCircuitStates.Add(new AiProviderCircuitState
            {
                ProviderScope = "job-description-extraction",
                Status = AiProviderCircuitStatus.Open,
                ConsecutiveFailures = 3,
                OpenUntil = DateTimeOffset.UtcNow.AddMinutes(5),
                Generation = 1,
                NextSequence = 3,
                LastFailureSequence = 3,
                ActiveProbes = 0
            });
            await context.SaveChangesAsync(cancellation.Token);
        }

        using var application = new ResuMatchWebApplicationFactory(Fixture);
        using var client = application.CreateHttpsClient();

        using var live = await client.GetAsync("/health/live", cancellation.Token);
        using var ready = await client.GetAsync("/health/ready", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task ProviderMaintenanceMode_IsReadyButJobParserIsDegraded()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["JobPostingParseDrafts:WorkerEnabled"] = "true",
                ["JobPostingParseDrafts:ProviderExecutionEnabled"] = "false",
                ["JobDescriptionExtraction:Provider"] = "Unavailable"
            });
        using var client = application.CreateHttpsClient();

        using var response = await client.GetAsync("/health/ready", cancellation.Token);
        var payload = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Degraded", payload, StringComparison.Ordinal);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
