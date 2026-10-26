using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Formatting;

public sealed class FormattingUrlRule : IAnalysisRule
{
    public const string Code = "ATS.FMT.004";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Formatting;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Formatting,
        IssueSeverity.Warning,
        "Links/Projects/Certifications");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var urls = GetPresentUrls(context).ToArray();
        if (urls.Length == 0)
        {
            return null;
        }

        var validCount = urls.Count(IsAbsoluteHttpUrl);
        return (decimal)validCount / urls.Length;
    }

    private static IEnumerable<string> GetPresentUrls(
        RuleEvaluationContext context)
    {
        var resume = context.Resume;

        if (!resume.Links.IsDefaultOrEmpty)
        {
            foreach (var link in resume.Links)
            {
                yield return link.Url ?? string.Empty;
            }
        }

        if (!resume.Projects.IsDefaultOrEmpty)
        {
            foreach (var project in resume.Projects)
            {
                if (RuleText.HasContent(project.Url))
                {
                    yield return project.Url!;
                }

                if (RuleText.HasContent(project.RepositoryUrl))
                {
                    yield return project.RepositoryUrl!;
                }
            }
        }

        if (!resume.Certifications.IsDefaultOrEmpty)
        {
            foreach (var certification in resume.Certifications)
            {
                if (RuleText.HasContent(certification.CredentialUrl))
                {
                    yield return certification.CredentialUrl!;
                }
            }
        }
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
