using System.Collections.Immutable;

namespace Api.Application.Analysis.Engine.Rules.Content;

public static class PlaceholderCatalog
{
    public const string Version = "ats-placeholders-v1";

    public static ImmutableHashSet<string> ExactValues { get; } =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "TODO",
            "TBD",
            "LOREM IPSUM",
            "XXX");

    public static bool IsExactPlaceholder(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ExactValues.Contains(RuleText.NormalizeForExactComparison(value));
    }
}
