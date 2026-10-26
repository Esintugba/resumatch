using Api.Entities.Enums;

namespace Api.DTOs.Skill;

public sealed class SkillResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public SkillCategory Category { get; init; }
}
