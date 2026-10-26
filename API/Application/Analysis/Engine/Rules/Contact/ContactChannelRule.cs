using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Contact;

public sealed class ContactChannelRule : IAnalysisRule
{
    public const string Code = "ATS.CON.005";

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

        var hasPhoneNumber =
            !string.IsNullOrWhiteSpace(context.Resume.Contact?.PhoneNumber);

        var hasProfessionalLink =
            !context.Resume.Links.IsDefaultOrEmpty &&
            context.Resume.Links.Any(link =>
                IsProfessionalLink(link.Type) &&
                !string.IsNullOrWhiteSpace(link.Url));

        return hasPhoneNumber || hasProfessionalLink ? 1m : 0m;
    }

    private static bool IsProfessionalLink(ResumeLinkType type)
    {
        return type is
            ResumeLinkType.LinkedIn or
            ResumeLinkType.GitHub or
            ResumeLinkType.Portfolio or
            ResumeLinkType.PersonalWebsite or
            ResumeLinkType.Behance or
            ResumeLinkType.Dribbble;
    }
}
