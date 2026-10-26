using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceProjectCompletenessRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.005";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Warning,
        "Projects");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projects = context.Resume.Projects;
        if (projects.IsDefaultOrEmpty)
        {
            return null;
        }

        var completeCount = projects.Count(project =>
            RuleText.HasContent(project.Description) &&
            !project.Skills.IsDefaultOrEmpty);

        return (decimal)completeCount / projects.Length;
    }
}
