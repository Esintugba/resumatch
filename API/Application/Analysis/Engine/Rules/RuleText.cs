using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Rules;

internal static class RuleText
{
    public static bool HasContent(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    public static string NormalizeWhitespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }

    public static string NormalizeForExactComparison(string value)
    {
        return NormalizeWhitespace(value).ToUpperInvariant();
    }

    public static bool IsApprovedEvidence(ExperienceBulletSnapshot bullet)
    {
        return !bullet.IsAiGenerated || bullet.IsUserApproved;
    }

    public static bool IsUsableEvidence(ExperienceBulletSnapshot bullet)
    {
        return IsApprovedEvidence(bullet) && HasContent(bullet.Content);
    }
}
