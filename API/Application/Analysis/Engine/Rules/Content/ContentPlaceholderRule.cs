using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentPlaceholderRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.005";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 10m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Content");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var content = GetScorableContent(context).ToArray();
        if (content.Length == 0)
        {
            return null;
        }

        return content.Any(PlaceholderCatalog.IsExactPlaceholder) ? 0m : 1m;
    }

    private static IEnumerable<string> GetScorableContent(
        RuleEvaluationContext context)
    {
        var resume = context.Resume;

        if (RuleText.HasContent(resume.Summary))
        {
            yield return resume.Summary!;
        }

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                if (RuleText.HasContent(experience.Description))
                {
                    yield return experience.Description!;
                }

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

        if (!resume.Projects.IsDefaultOrEmpty)
        {
            foreach (var project in resume.Projects)
            {
                if (RuleText.HasContent(project.Description))
                {
                    yield return project.Description!;
                }
            }
        }
    }
}
