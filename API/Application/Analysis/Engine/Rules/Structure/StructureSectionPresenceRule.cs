using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Structure;

public sealed class StructureSectionPresenceRule : IAnalysisRule
{
    public const string Code = "ATS.STR.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Structure;
    public decimal Weight => 30m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Structure,
        IssueSeverity.Critical,
        "Resume");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resume = context.Resume;
        return !resume.Experiences.IsDefaultOrEmpty ||
               !resume.Educations.IsDefaultOrEmpty ||
               !resume.Projects.IsDefaultOrEmpty
            ? 1m
            : 0m;
    }
}
