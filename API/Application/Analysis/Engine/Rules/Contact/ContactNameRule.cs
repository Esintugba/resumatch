using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Contact;

public sealed class ContactNameRule : IAnalysisRule
{
    public const string Code = "ATS.CON.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Contact;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.ContactInformation,
        IssueSeverity.Warning,
        "Contact");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contact = context.Resume.Contact;
        if (contact is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(contact.FirstName) &&
               !string.IsNullOrWhiteSpace(contact.LastName)
            ? 1m
            : 0m;
    }
}
