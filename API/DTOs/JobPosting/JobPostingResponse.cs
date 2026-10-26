using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed class JobPostingResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string? Location { get; init; }
    public WorkType? WorkType { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? SourceUrl { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public JobPostingStatus Status { get; init; }
    public string? ParsedSummary { get; init; }
    public List<JobPostingSkillResponse> Skills { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
}
