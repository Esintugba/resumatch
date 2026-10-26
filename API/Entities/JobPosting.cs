using Api.Entities.Enums;

namespace Api.Entities;

public class JobPosting : BaseEntity
{
    public uint Version { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Location { get; set; }

    public WorkType? WorkType { get; set; }
    public EmploymentType? EmploymentType { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public JobPostingStatus Status { get; set; }

    public string? ParsedSummary { get; set; }

    public User User { get; set; } = null!;

    public ICollection<JobPostingSkill> Skills { get; set; } = new List<JobPostingSkill>();
    public ICollection<ResumeAnalysis> ResumeAnalyses { get; set; } = new List<ResumeAnalysis>();
    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();
}
