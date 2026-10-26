using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentSummaryPresenceRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Summary");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RuleText.HasContent(context.Resume.Summary) ? 1m : 0m;
    }
}
