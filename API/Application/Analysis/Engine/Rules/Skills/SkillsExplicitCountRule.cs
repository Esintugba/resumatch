using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Skills;

public sealed class SkillsExplicitCountRule : IAnalysisRule
{
    public const string Code = "ATS.SKL.001";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Skills;
    public decimal Weight => 60m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Skill,
        IssueSeverity.Warning,
        "Skills");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var distinctSkillCount = context.Resume.Skills.IsDefaultOrEmpty
            ? 0
            : context.Resume.Skills
                .Select(resumeSkill => SkillCanonicalKey.From(resumeSkill.Skill))
                .Distinct()
                .Count();

        return distinctSkillCount switch
        {
            0 => 0m,
            <= 2 => 0.4m,
            <= 5 => 0.75m,
            <= 20 => 1m,
            <= 30 => 0.8m,
            _ => 0.6m
        };
    }
}
