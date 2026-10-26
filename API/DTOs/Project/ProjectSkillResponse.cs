using Api.Entities.Enums;

namespace Api.DTOs.Project;

public sealed class ProjectSkillResponse
{
    public Guid SkillId { get; init; }
    public string Name { get; init; } = string.Empty;
    public SkillCategory Category { get; init; }
}
