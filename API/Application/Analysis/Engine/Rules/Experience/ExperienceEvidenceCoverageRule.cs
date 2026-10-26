using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceEvidenceCoverageRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Warning,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var experiences = context.Resume.Experiences;
        if (experiences.IsDefaultOrEmpty)
        {
            return null;
        }

        var validCount = experiences.Count(experience =>
            RuleText.HasContent(experience.Description) ||
            (!experience.Bullets.IsDefaultOrEmpty &&
             experience.Bullets.Any(RuleText.IsUsableEvidence)));

        return (decimal)validCount / experiences.Length;
    }
}
