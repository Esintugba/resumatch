using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceQuantificationRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.004";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Info,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bulletContents = GetUsableBulletContents(context).ToArray();
        if (bulletContents.Length == 0)
        {
            return null;
        }

        var quantifiedCount = bulletContents.Count(
            ConservativeQuantificationDetector.ContainsQuantifiedOutcome);

        return (decimal)quantifiedCount / bulletContents.Length;
    }

    private static IEnumerable<string> GetUsableBulletContents(
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
                    yield return bullet.Content;
                }
            }
        }
    }
}
