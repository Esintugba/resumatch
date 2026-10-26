using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceBulletLengthRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Info,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bullets = GetUsableBullets(context).ToArray();
        if (bullets.Length == 0)
        {
            return null;
        }

        return bullets.Average(bullet => ScoreLength(
            RuleText.NormalizeWhitespace(bullet.Content).Length));
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

    private static decimal ScoreLength(int length)
    {
        return length switch
        {
            >= 20 and <= 350 => 1m,
            >= 10 and <= 19 => 0.5m,
            >= 351 and <= 500 => 0.5m,
            _ => 0m
        };
    }
}
