using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentRecordCoverageRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 25m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Experience/Projects");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applicableCount = 0;
        var validCount = 0;
        var resume = context.Resume;

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                applicableCount++;

                var hasEvidenceBullet =
                    !experience.Bullets.IsDefaultOrEmpty &&
                    experience.Bullets.Any(RuleText.IsUsableEvidence);

                if (RuleText.HasContent(experience.Description) ||
                    hasEvidenceBullet)
                {
                    validCount++;
                }
            }
        }

        if (!resume.Projects.IsDefaultOrEmpty)
        {
            foreach (var project in resume.Projects)
            {
                applicableCount++;
                if (RuleText.HasContent(project.Description))
                {
                    validCount++;
                }
            }
        }

        return applicableCount == 0
            ? null
            : (decimal)validCount / applicableCount;
    }
}
