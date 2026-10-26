using Api.Entities.Enums;

namespace Api.Entities;

public class JobPostingParseDraft : BaseEntity
{
    public Guid JobPostingId { get; set; }
    public Guid UserId { get; set; }

    public uint Version { get; set; }
    public uint SourceJobPostingVersion { get; set; }

    public string SourceTitle { get; set; } = string.Empty;
    public string SourceDescriptionHash { get; set; } = string.Empty;
    public string SourceInputHash { get; set; } = string.Empty;
    public string SourceDescription { get; set; } = string.Empty;
    public string? SkillCatalogHash { get; set; }
    public string ParserVersion { get; set; } = string.Empty;

    public string? ProviderRequestId { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderModel { get; set; }

    public JobPostingParseDraftStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public JobPosting JobPosting { get; set; } = null!;

    public ICollection<JobPostingParseDraftSkill> Skills { get; set; } =
        new List<JobPostingParseDraftSkill>();
}
