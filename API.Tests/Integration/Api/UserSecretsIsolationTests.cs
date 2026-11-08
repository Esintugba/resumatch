using System.Net;
using System.Reflection;
using System.Text.Json;
using Api.Application.JobParsing;
using Api.Infrastructure.Hosting;
using Api.Infrastructure.JobParsing;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api;

/// <summary>
/// Tests must never read a developer's user secrets (secrets.json), even when
/// they host the API in Development, where the default host loads them. Each
/// test points the user secrets location at a temporary folder holding a fake
/// secrets.json, so the real file is never touched.
/// </summary>
public sealed class UserSecretsIsolationTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ProbeKey = "UserSecretsProbe";
    private const string ProbeValue = "loaded-from-user-secrets";
    private const string FakeOrigin = "https://user-secrets.invalid";
    private const string DevelopmentOrigin = "http://localhost:5173";

    // Values a developer could plausibly keep in user secrets. Each one would
    // change what the host does if it were loaded: the CORS origin and the
    // extraction provider are read before the host is built.
    private const string FakeSecrets = $$"""
        {
          "{{ProbeKey}}": "{{ProbeValue}}",
          "HttpSecurity": { "AllowedOrigins": [ "{{FakeOrigin}}" ] },
          "JobDescriptionExtraction": {
            "Provider": "OpenAI",
            "ApiKey": "fake-user-secrets-key",
            "Model": "fake-user-secrets-model"
          }
        }
        """;

    [Fact]
    public void DefaultHost_LoadsTheFakeSecretsFile()
    {
        // Guards the other tests: without the isolation, a Development host
        // really would pick up this file, so its absence below means something.
        using var secrets = FakeUserSecretsLocation.Create(FakeSecrets);
        using var _ = secrets.Activate();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });

        Assert.Equal(ProbeValue, builder.Configuration[ProbeKey]);
    }

    [Fact]
    public void AllowedSourceList_RejectsUserSecrets()
    {
        // What the default host adds in Development, if Program did not remove it.
        using var secrets = FakeUserSecretsLocation.Create(FakeSecrets);
        using var _ = secrets.Activate();
        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(Program).Assembly, optional: true);

        Assert.Contains(builder.Sources, UserSecretsConfiguration.IsUserSecretsSource);
        var exception = Assert.Throws<InvalidOperationException>(
            () => TestConfigurationSources.Verify(builder.Sources, "Development"));
        Assert.Contains("secrets.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentHost_IgnoresUserSecrets()
    {
        using var secrets = FakeUserSecretsLocation.Create(FakeSecrets);
        var observed = await ObserveDevelopmentHostAsync(secrets);

        Assert.Null(observed.Probe);
        Assert.False(observed.HasUserSecretsProvider);
        Assert.Equal(nameof(UnavailableJobDescriptionExtractionProvider), observed.ExtractionProvider);
        Assert.True(observed.DevelopmentOriginAllowed);
        Assert.False(observed.FakeOriginAllowed);
    }

    [Fact]
    public async Task DevelopmentHost_BehavesTheSameWithAndWithoutASecretsFile()
    {
        using var withFile = FakeUserSecretsLocation.Create(FakeSecrets);
        using var withoutFile = FakeUserSecretsLocation.Create(secretsJson: null);

        var first = await ObserveDevelopmentHostAsync(withFile);
        var second = await ObserveDevelopmentHostAsync(withoutFile);

        Assert.Equal(second with { Configuration = [] }, first with { Configuration = [] });
        Assert.Equal(second.Configuration, first.Configuration);
    }

    private async Task<HostObservation> ObserveDevelopmentHostAsync(
        FakeUserSecretsLocation secrets)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            configurationOverrides: secrets.EnvironmentOverrides,
            environmentName: "Development");
        using var client = application.CreateHttpsClient();

        var configuration = application.Services.GetRequiredService<IConfiguration>();
        var providers = ((IConfigurationRoot)configuration).Providers.ToList();
        var extractionProvider = application.Services
            .GetRequiredService<IJobDescriptionExtractionProvider>()
            .GetType()
            .Name;

        return new HostObservation(
            Probe: configuration[ProbeKey],
            HasUserSecretsProvider: providers.Any(IsUserSecretsProvider),
            ExtractionProvider: extractionProvider,
            DevelopmentOriginAllowed: await IsOriginAllowedAsync(client, DevelopmentOrigin, cancellation.Token),
            FakeOriginAllowed: await IsOriginAllowedAsync(client, FakeOrigin, cancellation.Token),
            Configuration: SnapshotConfiguration(configuration, secrets));
    }

    private static bool IsUserSecretsProvider(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source.Path: "secrets.json" };

    private static async Task<bool> IsOriginAllowedAsync(
        HttpClient client,
        string origin,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK &&
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) &&
            values.Contains(origin);
    }

    // Every configuration value except the ones that name the temporary
    // folder, which differ between the two runs by design.
    private static SortedDictionary<string, string> SnapshotConfiguration(
        IConfiguration configuration,
        FakeUserSecretsLocation secrets)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (value is null || secrets.EnvironmentOverrides.ContainsKey(key))
            {
                continue;
            }

            snapshot[key] = value;
        }

        return snapshot;
    }

    private sealed record HostObservation(
        string? Probe,
        bool HasUserSecretsProvider,
        string ExtractionProvider,
        bool DevelopmentOriginAllowed,
        bool FakeOriginAllowed,
        SortedDictionary<string, string> Configuration)
    {
        public bool Equals(HostObservation? other) =>
            other is not null &&
            Probe == other.Probe &&
            HasUserSecretsProvider == other.HasUserSecretsProvider &&
            ExtractionProvider == other.ExtractionProvider &&
            DevelopmentOriginAllowed == other.DevelopmentOriginAllowed &&
            FakeOriginAllowed == other.FakeOriginAllowed &&
            Configuration.SequenceEqual(other.Configuration);

        public override int GetHashCode() => HashCode.Combine(Probe, ExtractionProvider);
    }

    /// <summary>
    /// A temporary folder laid out as the user secrets location on every OS:
    /// %APPDATA%\Microsoft\UserSecrets on Windows, ~/.microsoft/usersecrets
    /// elsewhere.
    /// </summary>
    private sealed class FakeUserSecretsLocation : IDisposable
    {
        private readonly string _root;

        private FakeUserSecretsLocation(string root)
        {
            _root = root;
            EnvironmentOverrides = new Dictionary<string, string?>
            {
                ["APPDATA"] = root,
                ["HOME"] = root
            };
        }

        public IReadOnlyDictionary<string, string?> EnvironmentOverrides { get; }

        public static FakeUserSecretsLocation Create(string? secretsJson)
        {
            var root = Path.Combine(Path.GetTempPath(), "resumatch-user-secrets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var id = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;
            foreach (var directory in new[]
            {
                Path.Combine(root, "Microsoft", "UserSecrets", id),
                Path.Combine(root, ".microsoft", "usersecrets", id)
            })
            {
                if (secretsJson is null)
                {
                    // No folder at all, as on a machine that never used
                    // user secrets (CI): the source then has no file provider.
                    continue;
                }

                Directory.CreateDirectory(directory);
                // Fails the test early if the fake file is not valid JSON.
                using var _ = JsonDocument.Parse(secretsJson);
                File.WriteAllText(Path.Combine(directory, "secrets.json"), secretsJson);
            }

            return new FakeUserSecretsLocation(root);
        }

        /// <summary>Points the user secrets location at this folder until disposed.</summary>
        public IDisposable Activate()
        {
            var previous = EnvironmentOverrides.Keys.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable);
            foreach (var (name, value) in EnvironmentOverrides)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            return new Restore(previous);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary folder is harmless.
            }
        }

        private sealed class Restore(Dictionary<string, string?> previous) : IDisposable
        {
            public void Dispose()
            {
                foreach (var (name, value) in previous)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }
    }
}
