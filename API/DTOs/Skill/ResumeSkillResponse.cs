using Api.Entities.Enums;

namespace Api.DTOs.Skill;

public sealed class ResumeSkillResponse
{
    public Guid SkillId { get; init; }
    public string Name { get; init; } = string.Empty;
    public SkillCategory Category { get; init; }
    public SkillLevel? Level { get; init; }
    public int SortOrder { get; init; }
    public bool IsHighlighted { get; init; }
}
