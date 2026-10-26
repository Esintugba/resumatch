using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeSkill : BaseEntity
{
    public Guid ResumeId { get; set; }
    public Guid SkillId { get; set; }

    public SkillLevel? Level { get; set; }

    public int SortOrder { get; set; }

    public bool IsHighlighted { get; set; }

    public Resume Resume { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
