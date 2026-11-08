using Microsoft.Extensions.Configuration.Json;

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

    /// <summary>
    /// Matches the source by its file name. When the user secrets folder does
    /// not exist, AddUserSecrets adds the source without a file provider and it
    /// falls back to the content root, so the folder cannot be compared. This
    /// application has no other secrets.json source.
    /// </summary>
    public static bool IsUserSecretsSource(IConfigurationSource source) =>
        source is JsonConfigurationSource { Path: var path } &&
        string.Equals(Path.GetFileName(path), "secrets.json", StringComparison.OrdinalIgnoreCase);
}
