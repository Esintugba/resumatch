using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentExactDuplicateRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.004";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Experience/Projects");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var normalizedContent = GetScorableContent(context).ToArray();
        if (normalizedContent.Length == 0)
        {
            return null;
        }

        return normalizedContent.Distinct(StringComparer.Ordinal).Count() ==
               normalizedContent.Length
            ? 1m
            : 0m;
    }

    private static IEnumerable<string> GetScorableContent(
        RuleEvaluationContext context)
    {
        var resume = context.Resume;

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                if (experience.Bullets.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var bullet in experience.Bullets)
                {
                    if (RuleText.IsUsableEvidence(bullet))
                    {
                        yield return RuleText.NormalizeForExactComparison(
                            bullet.Content);
                    }
                }
            }
        }

        if (!resume.Projects.IsDefaultOrEmpty)
        {
            foreach (var project in resume.Projects)
            {
                if (RuleText.HasContent(project.Description))
                {
                    yield return RuleText.NormalizeForExactComparison(
                        project.Description!);
                }
            }
        }
    }
}
