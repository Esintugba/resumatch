using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common;
using Api.DTOs.Common.Validation;
using Api.Entities.Enums;

namespace Api.DTOs.Experience;

[DateOnlyOrder(nameof(StartDate), nameof(EndDate))]
[NullWhenTrue(nameof(IsCurrent), nameof(EndDate))]
public sealed class CreateExperienceRequest
{
    [Required]
    [StringLength(200)]
    public string CompanyName { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Position { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Location { get; init; }

    [Required]
    [EnumDataType(typeof(EmploymentType))]
    public EmploymentType? EmploymentType { get; init; }

    [Required]
    [DateOnlyRange(1900)]
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public bool IsCurrent { get; init; }

    [StringLength(5000)]
    public string? Description { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    [MaxLength(DtoCollectionLimits.ExperienceBullets)]
    public List<ExperienceBulletRequest> Bullets { get; init; } = new();
}
