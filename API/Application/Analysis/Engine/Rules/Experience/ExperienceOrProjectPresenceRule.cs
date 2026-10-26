using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Experience;

public sealed class ExperienceOrProjectPresenceRule : IAnalysisRule
{
    public const string Code = "ATS.EXP.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Experience;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Experience,
        IssueSeverity.Critical,
        "Experience/Projects");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return !context.Resume.Experiences.IsDefaultOrEmpty ||
               !context.Resume.Projects.IsDefaultOrEmpty
            ? 1m
            : 0m;
    }
}
