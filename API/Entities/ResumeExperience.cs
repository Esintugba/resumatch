using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeExperience : BaseEntity
{
    public uint Version { get; set; }

    public Guid ResumeId { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? Location { get; set; }

    public EmploymentType EmploymentType { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public Resume Resume { get; set; } = null!;
    public ICollection<ExperienceBullet> Bullets { get; set; } = new List<ExperienceBullet>();
}
