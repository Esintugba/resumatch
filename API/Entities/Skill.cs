using Api.Entities.Enums;

namespace Api.Entities;

public class Skill : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;

    public SkillCategory Category { get; set; }
}
