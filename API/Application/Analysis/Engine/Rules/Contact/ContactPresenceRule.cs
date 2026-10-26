using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Contact;

public sealed class ContactPresenceRule : IAnalysisRule
{
    public const string Code = "ATS.CON.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Contact;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.ContactInformation,
        IssueSeverity.Critical,
        "Contact");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Resume.Contact is null ? 0m : 1m;
    }
}
