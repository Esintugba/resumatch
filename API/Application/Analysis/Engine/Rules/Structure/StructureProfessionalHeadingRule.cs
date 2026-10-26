using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Structure;

public sealed class StructureProfessionalHeadingRule : IAnalysisRule
{
    public const string Code = "ATS.STR.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Structure;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Structure,
        IssueSeverity.Warning,
        "Header");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resume = context.Resume;
        return !string.IsNullOrWhiteSpace(resume.ProfessionalTitle) ||
               !string.IsNullOrWhiteSpace(resume.TargetRole)
            ? 1m
            : 0m;
    }
}
