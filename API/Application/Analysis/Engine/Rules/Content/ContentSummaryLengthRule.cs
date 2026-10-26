using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentSummaryLengthRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Summary");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var summary = context.Resume.Summary;
        if (!RuleText.HasContent(summary))
        {
            return null;
        }

        var length = RuleText.NormalizeWhitespace(summary!).Length;
        return length switch
        {
            >= 80 and <= 800 => 1m,
            >= 40 and <= 79 => 0.5m,
            >= 801 and <= 1200 => 0.5m,
            _ => 0m
        };
    }
}
