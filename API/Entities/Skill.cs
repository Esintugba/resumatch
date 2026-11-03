using Api.Entities.Enums;

namespace Api.Entities;

public class Skill : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? NameTr { get; set; }
    public string? NormalizedNameTr { get; set; }

    // Folded search terms that point to this skill. Search only; never matched.
    public string[] SearchAliases { get; set; } = [];

    public SkillCategory Category { get; set; }
}
