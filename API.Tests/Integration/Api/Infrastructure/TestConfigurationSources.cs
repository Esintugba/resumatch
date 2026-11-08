using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace API.Tests.Integration.Api.Infrastructure;

/// <summary>
/// The configuration sources a test host may use, stated explicitly. Anything
/// else, user secrets above all, stops the host before a test runs, so a new
/// source cannot slip real settings into tests on some machine.
/// </summary>
internal static class TestConfigurationSources
{
    public static void Verify(IConfiguration configuration, string environmentName) =>
        Verify(Flatten(configuration).Select(provider => provider switch
        {
            JsonConfigurationProvider json => (IConfigurationSource)json.Source,
            MemoryConfigurationProvider => new MemoryConfigurationSource(),
            EnvironmentVariablesConfigurationProvider => new EnvironmentVariablesConfigurationSource(),
            CommandLineConfigurationProvider => new CommandLineConfigurationSource(),
            _ => new UnknownSource(provider.GetType().Name)
        }), environmentName);

    public static void Verify(IEnumerable<IConfigurationSource> sources, string environmentName)
    {
        // The API's settings files, and the optional {assembly}.settings files
        // that WebApplicationFactory adds for test-specific settings.
        string[] allowedJsonFiles =
        [
            "appsettings.json",
            $"appsettings.{environmentName}.json",
            "API.settings.json",
            $"API.settings.{environmentName}.json"
        ];
        var unexpected = sources
            .Where(source => source switch
            {
                // Host defaults, the factory's own settings and test overrides.
                MemoryConfigurationSource => false,
                EnvironmentVariablesConfigurationSource => false,
                CommandLineConfigurationSource => false,
                JsonConfigurationSource json => !allowedJsonFiles.Contains(json.Path, StringComparer.OrdinalIgnoreCase),
                _ => true
            })
            .Select(Describe)
            .ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException(
                "The test host has configuration sources outside the allowed list: " +
                string.Join(", ", unexpected) +
                ". User secrets must be removed by Hosting:IgnoreUserSecrets.");
        }
    }

    private static IEnumerable<IConfigurationProvider> Flatten(IConfiguration configuration) =>
        configuration is IConfigurationRoot root
            ? root.Providers.SelectMany(provider => provider is ChainedConfigurationProvider chained
                ? Flatten(chained.Configuration)
                : [provider])
            : [];

    private static string Describe(IConfigurationSource source) => source switch
    {
        JsonConfigurationSource json => $"JSON file '{json.Path}'",
        UnknownSource unknown => unknown.Name,
        _ => source.GetType().Name
    };

    private sealed class UnknownSource(string name) : IConfigurationSource
    {
        public string Name { get; } = name;

        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            throw new NotSupportedException();
    }
}
