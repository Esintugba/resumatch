using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Skills;

public sealed class SkillsProjectRepresentationRule : IAnalysisRule
{
    public const string Code = "ATS.SKL.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Skills;
    public decimal Weight => 15m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Skill,
        IssueSeverity.Info,
        "Skills/Projects");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectSkillKeys = GetProjectSkillKeys(context).Distinct().ToArray();
        if (projectSkillKeys.Length == 0)
        {
            return null;
        }

        var explicitSkillKeys = context.Resume.Skills.IsDefaultOrEmpty
            ? new HashSet<SkillCanonicalKey>()
            : context.Resume.Skills
                .Select(resumeSkill => SkillCanonicalKey.From(resumeSkill.Skill))
                .ToHashSet();

        var representedCount = projectSkillKeys.Count(explicitSkillKeys.Contains);
        return (decimal)representedCount / projectSkillKeys.Length;
    }

    private static IEnumerable<SkillCanonicalKey> GetProjectSkillKeys(
        RuleEvaluationContext context)
    {
        if (context.Resume.Projects.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (var project in context.Resume.Projects)
        {
            if (project.Skills.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var projectSkill in project.Skills)
            {
                yield return SkillCanonicalKey.From(projectSkill.Skill);
            }
        }
    }
}
