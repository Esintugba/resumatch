using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed class JobPostingListItemResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string? Location { get; init; }
    public WorkType? WorkType { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public JobPostingStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
