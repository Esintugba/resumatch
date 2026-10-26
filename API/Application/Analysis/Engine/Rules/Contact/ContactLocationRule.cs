using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Contact;

public sealed class ContactLocationRule : IAnalysisRule
{
    public const string Code = "ATS.CON.004";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Contact;
    public decimal Weight => 10m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.ContactInformation,
        IssueSeverity.Info,
        "Contact");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contact = context.Resume.Contact;
        if (contact is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(contact.City) ||
               !string.IsNullOrWhiteSpace(contact.Country)
            ? 1m
            : 0m;
    }
}
