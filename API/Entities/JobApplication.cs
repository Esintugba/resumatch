using Api.Entities.Enums;

namespace Api.Entities;

public class JobApplication : BaseEntity
{
    public uint Version { get; set; }

    public Guid UserId { get; set; }
    public Guid JobPostingId { get; set; }
    public Guid? ResumeId { get; set; }
    public Guid? ResumeUserId { get; set; }

    public ApplicationStatus Status { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? NextActionAt { get; set; }

    public User User { get; set; } = null!;
    public JobPosting JobPosting { get; set; } = null!;
    public Resume? Resume { get; set; }
}
