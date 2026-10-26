using Api.Entities.Enums;

namespace Api.DTOs.Experience;

public sealed class ExperienceResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string? Location { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public bool IsCurrent { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public List<ExperienceBulletResponse> Bullets { get; init; } = new();
}
