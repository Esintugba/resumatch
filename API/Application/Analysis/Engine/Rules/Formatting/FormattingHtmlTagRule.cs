using System.Text.RegularExpressions;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Formatting;

public sealed partial class FormattingHtmlTagRule : IAnalysisRule
{
    public const string Code = "ATS.FMT.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Formatting;
    public decimal Weight => 25m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Formatting,
        IssueSeverity.Critical,
        "Resume");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var textValues = ResumeTextValues.Enumerate(context).ToArray();
        if (textValues.Length == 0)
        {
            return null;
        }

        return textValues.Any(value => HtmlTagPattern().IsMatch(value))
            ? 0m
            : 1m;
    }

    [GeneratedRegex(
        @"</?(?:script|style|iframe|object|embed|html|body|head|div|span|p|br|table|thead|tbody|tfoot|tr|td|th|ul|ol|li|a|img|h[1-6]|form|input|button|link|meta)(?:\s+[^<>]*)?\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagPattern();
}
