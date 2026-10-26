using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace API.Tests.Integration.Api;

public sealed class ObservabilityHostingTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public void EnabledTelemetry_RegistersTraceAndMetricPipelinesWithoutOtlp()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:TracingEnabled"] = "true",
                ["Observability:MetricsEnabled"] = "true",
                ["Observability:OtlpEnabled"] = "false",
                ["Observability:OperationalSamplerEnabled"] = "false"
            });

        Assert.NotNull(application.Services.GetService<TracerProvider>());
        Assert.NotNull(application.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void DisabledTelemetry_DoesNotStartOpenTelemetryProviders()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "false",
                ["Observability:OtlpEnabled"] = "false"
            });

        Assert.Null(application.Services.GetService<TracerProvider>());
        Assert.Null(application.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void InvalidEnabledOtlpEndpoint_FailsStartup()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:OtlpEnabled"] = "true",
                ["Observability:OtlpEndpoint"] = "file:///tmp/collector"
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _ = application.Services);
        Assert.Contains("OtlpEndpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerAndProviderExecutionEnabled_WithUnavailableProvider_FailsStartup()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["JobPostingParseDrafts:WorkerEnabled"] = "true",
                ["JobPostingParseDrafts:ProviderExecutionEnabled"] = "true",
                ["JobDescriptionExtraction:Provider"] = "Unavailable"
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = application.Services);
        Assert.Contains(
            "worker",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecureOtlpPolicy_WithHttpEndpoint_FailsStartupWithoutConnecting()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:OtlpEnabled"] = "true",
                ["Observability:OtlpEndpoint"] = "http://127.0.0.1:1",
                ["Observability:RequireSecureOtlpTransport"] = "true"
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _ = application.Services);
        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableOtlpCollector_DoesNotBreakCoreHttpOrReadiness()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:TracingEnabled"] = "true",
                ["Observability:MetricsEnabled"] = "true",
                ["Observability:OtlpEnabled"] = "true",
                ["Observability:OtlpEndpoint"] = "http://127.0.0.1:1",
                ["Observability:OtlpExportTimeout"] = "00:00:00.250",
                ["Observability:OperationalSamplerEnabled"] = "false"
            });
        using var client = application.CreateHttpsClient();

        using var live = await client.GetAsync("/health/live", cancellation.Token);
        using var ready = await client.GetAsync("/health/ready", cancellation.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public void ProductionProviderExecution_RequiresExplicitPinnedModelPolicy()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["JobPostingParseDrafts:WorkerEnabled"] = "true",
                ["JobPostingParseDrafts:ProviderExecutionEnabled"] = "true",
                ["JobDescriptionExtraction:Provider"] = "OpenAI",
                ["JobDescriptionExtraction:ApiKey"] = "test-only-key",
                ["JobDescriptionExtraction:Model"] = "operator-model",
                ["JobDescriptionExtraction:RequirePinnedModel"] = "false",
                ["JobDescriptionExtraction:ModelIsPinned"] = "false"
            },
            environmentName: "Production");

        var exception = Assert.Throws<InvalidOperationException>(
            () => _ = application.Services);
        Assert.Contains("pinned-model", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-only-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidProductionLikeConfiguration_StartsWithoutCallingProvider()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["JobPostingParseDrafts:WorkerEnabled"] = "true",
                ["JobPostingParseDrafts:ProviderExecutionEnabled"] = "true",
                ["JobDescriptionExtraction:Provider"] = "OpenAI",
                ["JobDescriptionExtraction:ApiKey"] = "test-only-key",
                ["JobDescriptionExtraction:Model"] = "operator-pinned-model",
                ["JobDescriptionExtraction:RequirePinnedModel"] = "true",
                ["JobDescriptionExtraction:ModelIsPinned"] = "true",
                ["Observability:Enabled"] = "true",
                ["Observability:OtlpEnabled"] = "false"
            },
            environmentName: "Production");

        Assert.NotNull(application.Services);
    }

    [Fact]
    public void HostShutdownTimeout_IsDerivedFromWorkerGracePeriod()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                ["JobPostingParseDrafts:ShutdownGracePeriod"] = "00:00:12"
            });

        var hostOptions = application.Services
            .GetRequiredService<IOptions<HostOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromSeconds(17), hostOptions.ShutdownTimeout);
        Assert.True(hostOptions.ServicesStopConcurrently);
    }
}
