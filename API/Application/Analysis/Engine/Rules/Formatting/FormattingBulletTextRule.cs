using System.Text.RegularExpressions;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Formatting;

public sealed partial class FormattingBulletTextRule : IAnalysisRule
{
    private const int MaximumLineBreakCount = 2;

    public const string Code = "ATS.FMT.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Formatting;
    public decimal Weight => 30m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Formatting,
        IssueSeverity.Warning,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bullets = GetUsableBullets(context).ToArray();
        if (bullets.Length == 0)
        {
            return null;
        }

        var validCount = bullets.Count(bullet => IsSafelyFormatted(bullet.Content));
        return (decimal)validCount / bullets.Length;
    }

    private static bool IsSafelyFormatted(string content)
    {
        var lineBreakCount = LineBreakPattern().Count(content);
        if (lineBreakCount > MaximumLineBreakCount ||
            MultipleBlankLinesPattern().IsMatch(content))
        {
            return false;
        }

        return PastedBulletMarkerPattern().Count(content) < 2;
    }

    private static IEnumerable<ExperienceBulletSnapshot> GetUsableBullets(
        RuleEvaluationContext context)
    {
        if (context.Resume.Experiences.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (var experience in context.Resume.Experiences)
        {
            if (experience.Bullets.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var bullet in experience.Bullets)
            {
                if (RuleText.IsUsableEvidence(bullet))
                {
                    yield return bullet;
                }
            }
        }
    }

    [GeneratedRegex(@"\r\n|\r|\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(
        @"(?:\r\n|\r|\n)[ \t]*(?:\r\n|\r|\n)[ \t]*(?:\r\n|\r|\n)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MultipleBlankLinesPattern();

    [GeneratedRegex(
        @"^[ \t]*(?:[-*•▪◦]|\d+[.)])[ \t]+",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PastedBulletMarkerPattern();
}
