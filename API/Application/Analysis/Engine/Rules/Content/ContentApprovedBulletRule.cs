using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Content;

public sealed class ContentApprovedBulletRule : IAnalysisRule
{
    public const string Code = "ATS.CNT.006";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Content;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Content,
        IssueSeverity.Warning,
        "Experience");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bullets = context.Resume.Experiences.IsDefaultOrEmpty
            ? []
            : context.Resume.Experiences
                .Where(experience => !experience.Bullets.IsDefaultOrEmpty)
                .SelectMany(experience => experience.Bullets)
                .ToArray();

        if (bullets.Length == 0)
        {
            return null;
        }

        var approvedCount = bullets.Count(RuleText.IsApprovedEvidence);
        return (decimal)approvedCount / bullets.Length;
    }
}
