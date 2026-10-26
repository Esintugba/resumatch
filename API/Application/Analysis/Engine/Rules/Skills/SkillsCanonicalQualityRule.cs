using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Skills;

public sealed class SkillsCanonicalQualityRule : IAnalysisRule
{
    public const string Code = "ATS.SKL.002";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Skills;
    public decimal Weight => 25m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Skill,
        IssueSeverity.Warning,
        "Skills");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resumeSkills = context.Resume.Skills;
        if (resumeSkills.IsDefaultOrEmpty)
        {
            return null;
        }

        var canonicalCounts = resumeSkills
            .GroupBy(resumeSkill => SkillCanonicalKey.From(resumeSkill.Skill))
            .ToDictionary(group => group.Key, group => group.Count());

        var validCount = resumeSkills.Count(resumeSkill =>
        {
            var skill = resumeSkill.Skill;
            var key = SkillCanonicalKey.From(skill);

            return RuleText.HasContent(skill.NormalizedName) &&
                   Enum.IsDefined(skill.Category) &&
                   canonicalCounts[key] == 1;
        });

        return (decimal)validCount / resumeSkills.Length;
    }
}
