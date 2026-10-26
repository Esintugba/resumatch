using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceBulletCountRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.006";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 10m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Info,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var experiences = context.Resume.Experiences;
        if (experiences.IsDefaultOrEmpty)
        {
            return null;
        }

        return experiences.Average(experience =>
        {
            var usableBulletCount = experience.Bullets.IsDefaultOrEmpty
                ? 0
                : experience.Bullets.Count(RuleText.IsUsableEvidence);

            return usableBulletCount switch
            {
                >= 2 and <= 6 => 1m,
                1 or 7 or 8 => 0.5m,
                _ => 0m
            };
        });
    }
}
