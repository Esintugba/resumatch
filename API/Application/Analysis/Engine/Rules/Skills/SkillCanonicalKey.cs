using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Skills;

internal readonly record struct SkillCanonicalKey(
    string NormalizedName,
    SkillCategory Category)
{
    public static SkillCanonicalKey From(SkillSnapshot skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var nameSource = RuleText.HasContent(skill.NormalizedName)
            ? skill.NormalizedName
            : skill.Name;

        var normalizedName = RuleText.HasContent(nameSource)
            ? RuleText.NormalizeForExactComparison(nameSource)
            : string.Empty;

        return new SkillCanonicalKey(normalizedName, skill.Category);
    }
}
