using Api.Entities.Enums;

namespace Api.DTOs.JobApplication;

public sealed class JobApplicationListItemResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public Guid JobPostingId { get; init; }
    public string JobTitle { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public Guid? ResumeId { get; init; }
    public string? ResumeTitle { get; init; }
    public ApplicationStatus Status { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public DateTimeOffset? NextActionAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
