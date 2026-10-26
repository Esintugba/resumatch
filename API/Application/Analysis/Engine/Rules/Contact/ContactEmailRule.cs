using System.ComponentModel.DataAnnotations;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Contact;

public sealed class ContactEmailRule : IAnalysisRule
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public const string Code = "ATS.CON.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Contact;
    public decimal Weight => 40m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.ContactInformation,
        IssueSeverity.Critical,
        "Contact");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contact = context.Resume.Contact;
        if (contact is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(contact.Email) &&
               EmailValidator.IsValid(contact.Email)
            ? 1m
            : 0m;
    }
}
