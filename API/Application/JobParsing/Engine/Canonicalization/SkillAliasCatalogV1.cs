using System.Collections.Immutable;

namespace Api.Application.JobParsing.Engine.Canonicalization;

public static class SkillAliasCatalogV1
{
    public const string Version = "skill-aliases-v1";

    private static readonly ImmutableDictionary<string, string> AliasMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["postgres"] = "postgresql",
            ["js"] = "javascript",
            ["ts"] = "typescript"
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Aliases => AliasMap;

    public static bool TryResolve(
        string normalizedAlias,
        out string canonicalName)
    {
        ArgumentNullException.ThrowIfNull(normalizedAlias);
        return AliasMap.TryGetValue(normalizedAlias, out canonicalName!);
    }
}
