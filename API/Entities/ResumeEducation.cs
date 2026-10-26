namespace Api.Entities;

public class ResumeEducation : BaseEntity
{
    public Guid ResumeId { get; set; }

    public string InstitutionName { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public string? Location { get; set; }

    public decimal? Grade { get; set; }
    public string? GradeScale { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public Resume Resume { get; set; } = null!;
}
