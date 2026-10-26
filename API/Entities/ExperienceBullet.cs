namespace Api.Entities;

public class ExperienceBullet : BaseEntity
{
    public Guid ResumeExperienceId { get; set; }

    public string Content { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsAiGenerated { get; set; }
    public bool IsUserApproved { get; set; }

    public string? OriginalContent { get; set; }

    public ResumeExperience ResumeExperience { get; set; } = null!;
}
