using System.ComponentModel.DataAnnotations;
using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed class CreateJobPostingRequest
{
    [Required]
    [StringLength(250)]
    public string Title { get; init; } = string.Empty;

    [StringLength(250)]
    public string? CompanyName { get; init; }

    [StringLength(250)]
    public string? Location { get; init; }

    [EnumDataType(typeof(WorkType))]
    public WorkType? WorkType { get; init; }

    [EnumDataType(typeof(EmploymentType))]
    public EmploymentType? EmploymentType { get; init; }

    [Required]
    public string Description { get; init; } = string.Empty;

    [Url]
    [StringLength(2000)]
    public string? SourceUrl { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
