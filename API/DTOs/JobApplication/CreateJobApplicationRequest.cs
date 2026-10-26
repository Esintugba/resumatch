using Api.DTOs.Common.Validation;

namespace Api.DTOs.JobApplication;

public sealed class CreateJobApplicationRequest
{
    [NotEmptyGuid]
    public Guid JobPostingId { get; init; }

    [NotEmptyGuid]
    public Guid? ResumeId { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? NextActionAt { get; init; }
}
