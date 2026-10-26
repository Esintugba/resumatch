namespace Api.DTOs.Project;

public sealed class ProjectResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? RepositoryUrl { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int SortOrder { get; init; }
    public List<ProjectSkillResponse> Skills { get; init; } = new();
}
