namespace Api.Entities;

public class ProjectSkill : BaseEntity
{
    public Guid ResumeProjectId { get; set; }
    public Guid SkillId { get; set; }

    public ResumeProject Project { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
