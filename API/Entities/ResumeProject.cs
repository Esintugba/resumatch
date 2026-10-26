namespace Api.Entities;

public class ResumeProject : BaseEntity
{
    public uint Version { get; set; }

    public Guid ResumeId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string? Url { get; set; }
    public string? RepositoryUrl { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public int SortOrder { get; set; }

    public Resume Resume { get; set; } = null!;
    public ICollection<ProjectSkill> Skills { get; set; } = new List<ProjectSkill>();
}
