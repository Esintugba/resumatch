using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Formatting;

public sealed class FormattingControlCharacterRule : IAnalysisRule
{
    public const string Code = "ATS.FMT.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Formatting;
    public decimal Weight => 30m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Formatting,
        IssueSeverity.Warning,
        "Resume");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var textValues = ResumeTextValues.Enumerate(context).ToArray();
        if (textValues.Length == 0)
        {
            return null;
        }

        return textValues.Any(ContainsProblematicControlCharacter)
            ? 0m
            : 1m;
    }

    private static bool ContainsProblematicControlCharacter(string value)
    {
        return value.Any(character =>
            char.IsControl(character) &&
            character is not '\r' and not '\n' and not '\t');
    }
}
