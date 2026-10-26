namespace Api.DTOs.Education;

public sealed class EducationResponse
{
    public Guid Id { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string? Degree { get; init; }
    public string? FieldOfStudy { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Location { get; init; }
    public decimal? Grade { get; init; }
    public string? GradeScale { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}
