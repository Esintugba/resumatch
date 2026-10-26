using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;
using Api.Entities.Enums;

namespace Api.DTOs.JobApplication;

public sealed class UpdateJobApplicationRequest
{
    [Required]
    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus? Status { get; init; }

    [NotEmptyGuid]
    public Guid? ResumeId { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? NextActionAt { get; init; }
}
