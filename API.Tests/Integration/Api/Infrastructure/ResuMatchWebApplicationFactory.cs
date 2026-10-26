using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api.Infrastructure;

public sealed class ResuMatchWebApplicationFactory
    : WebApplicationFactory<Program>
{
    public const string TestIssuer = "ResuMatch.IntegrationTests";
    public const string TestAudience = "ResuMatch.IntegrationTests.Client";
    public const string TestSigningSecret =
        "pALwOlvmqn7BAJ71JrIkCUTmP8KR1unQcygMP5yaVHc=";
    public const int TestAccessTokenExpirationMinutes = 15;

    private static readonly SemaphoreSlim EnvironmentLease = new(1, 1);

    private readonly PostgreSqlFixture _fixture;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly HttpMessageHandler? _openAiHandler;
    private readonly Action<IServiceCollection>? _configureTestServices;
    private readonly string _environmentName;
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private bool _environmentRestored;

    public ResuMatchWebApplicationFactory(
        PostgreSqlFixture fixture,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        HttpMessageHandler? openAiHandler = null,
        Action<IServiceCollection>? configureTestServices = null,
        string environmentName = "Testing")
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        _configurationOverrides = configurationOverrides ??
            new Dictionary<string, string?>();
        _openAiHandler = openAiHandler;
        _configureTestServices = configureTestServices;
        _environmentName = environmentName;

        EnvironmentLease.Wait();
        try
        {
            SetEnvironment("DOTNET_ENVIRONMENT", _environmentName);
            SetEnvironment("ASPNETCORE_ENVIRONMENT", _environmentName);
            SetEnvironment(
                "ConnectionStrings__DefaultConnection",
                _fixture.ConnectionString);
            SetEnvironment("Jwt__Issuer", TestIssuer);
            SetEnvironment("Jwt__Audience", TestAudience);
            SetEnvironment("Jwt__SecretKey", TestSigningSecret);
            SetEnvironment(
                "Jwt__AccessTokenExpirationMinutes",
                TestAccessTokenExpirationMinutes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            SetEnvironment("RequestLimits__MaxRequestBodySizeBytes", "1048576");
            foreach (var (key, value) in _configurationOverrides)
            {
                SetEnvironment(
                    key.Replace(":", "__", StringComparison.Ordinal),
                    value ?? string.Empty);
            }
        }
        catch
        {
            RestoreEnvironment();
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _fixture.ConnectionString,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:SecretKey"] = TestSigningSecret,
                ["Jwt:AccessTokenExpirationMinutes"] =
                    TestAccessTokenExpirationMinutes.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                ["RequestLimits:MaxRequestBodySizeBytes"] = "1048576"
            };
            if (_openAiHandler is not null)
            {
                values["JobDescriptionExtraction:Provider"] = "OpenAI";
                values["JobDescriptionExtraction:Model"] =
                    "test-structured-output-model";
                values["JobDescriptionExtraction:ApiKey"] =
                    "test-only-non-production-key";
                values["JobDescriptionExtraction:MaxInputCharacters"] = "50250";
                values["JobDescriptionExtraction:MaxOutputTokens"] = "4096";
                values["JobDescriptionExtraction:MaxConcurrentRequests"] = "1";
                values["JobDescriptionExtraction:RequestTimeout"] = "00:00:05";
                values["JobPostingParseDrafts:WorkerEnabled"] = "false";
                values["JobPostingParseDrafts:ProviderExecutionEnabled"] = "true";
            }

            foreach (var (key, value) in _configurationOverrides)
            {
                values[key] = value;
            }

            configuration.AddInMemoryCollection(values);
        });

        if (_openAiHandler is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.TryAddSingleton<ProviderFacingResponseJsonValidator>();
                services.TryAddSingleton<EvidenceOffsetResolver>();
                services.TryAddSingleton<ProviderExtractionPayloadBuilder>();
                services.RemoveAll<IJobDescriptionExtractionProvider>();
                services.AddSingleton<IJobDescriptionExtractionProvider>(
                    serviceProvider => new OpenAiJobDescriptionExtractionProvider(
                        new HttpClient(_openAiHandler, disposeHandler: false)
                        {
                            Timeout = Timeout.InfiniteTimeSpan
                        },
                        serviceProvider.GetRequiredService<IOptions<
                            OpenAiJobDescriptionExtractionOptions>>(),
                        serviceProvider.GetRequiredService<
                            ProviderFacingResponseJsonValidator>(),
                        serviceProvider.GetRequiredService<
                            ProviderExtractionPayloadBuilder>(),
                        serviceProvider.GetRequiredService<TimeProvider>(),
                        serviceProvider.GetRequiredService<ILogger<
                            OpenAiJobDescriptionExtractionProvider>>()));
            });
        }

        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                RestoreEnvironment();
            }
        }
    }

    private void SetEnvironment(string name, string value)
    {
        _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironment()
    {
        if (_environmentRestored)
        {
            return;
        }

        foreach (var (name, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _environmentRestored = true;
        EnvironmentLease.Release();
    }
}
