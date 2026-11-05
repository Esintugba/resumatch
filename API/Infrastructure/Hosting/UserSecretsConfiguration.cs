using System.Reflection;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.FileProviders;

namespace Api.Infrastructure.Hosting;

/// <summary>
/// The default host adds the developer's user secrets (secrets.json) in
/// Development before any application code runs. Tests host the API in
/// Development too and must never read that file, so they set
/// <see cref="IgnoreSwitch"/> and the source is removed before the first
/// setting is read.
/// </summary>
public static class UserSecretsConfiguration
{
    /// <summary>Set to <c>true</c> (environment variable <c>Hosting__IgnoreUserSecrets</c>) to drop user secrets.</summary>
    public const string IgnoreSwitch = "Hosting:IgnoreUserSecrets";

    public static void RemoveWhenRequested(ConfigurationManager configuration)
    {
        if (!configuration.GetValue<bool>(IgnoreSwitch))
        {
            return;
        }

        IConfigurationBuilder builder = configuration;
        foreach (var source in builder.Sources.Where(IsUserSecretsSource).ToList())
        {
            // ConfigurationManager reloads on removal, so no value from the
            // file remains visible.
            builder.Sources.Remove(source);
        }
    }

    public static bool IsUserSecretsSource(IConfigurationSource source)
    {
        if (source is not JsonConfigurationSource { Path: "secrets.json", FileProvider: PhysicalFileProvider provider })
        {
            return false;
        }

        var id = typeof(UserSecretsConfiguration).Assembly
            .GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
        if (id is null)
        {
            return false;
        }

        var expected = Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(id));
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(provider.Root)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected!)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
