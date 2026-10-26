using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.Education;

[DateOnlyOrder(nameof(StartDate), nameof(EndDate))]
public sealed class UpdateEducationRequest
{
    [Required]
    [StringLength(250)]
    public string InstitutionName { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Degree { get; init; }

    [StringLength(200)]
    public string? FieldOfStudy { get; init; }

    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    [StringLength(200)]
    public string? Location { get; init; }

    [DecimalPrecision(5, 2)]
    public decimal? Grade { get; init; }

    [StringLength(20)]
    public string? GradeScale { get; init; }

    [StringLength(3000)]
    public string? Description { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}
