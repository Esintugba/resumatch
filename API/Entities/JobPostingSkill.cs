using Api.Entities.Enums;

namespace Api.Entities;

public class JobPostingSkill : BaseEntity
{
    private JobPostingSkillProvenanceStatus? _provenanceStatus;

    public Guid JobPostingId { get; set; }
    public Guid SkillId { get; set; }

    public JobSkillRequirementType RequirementType { get; set; }

    // Expected range: 0-100.
    public int ImportanceScore { get; set; }

    public bool IsAiDetected { get; set; }

    public JobPostingSkillProvenanceStatus ProvenanceStatus
    {
        get => _provenanceStatus ?? (IsAiDetected
            ? JobPostingSkillProvenanceStatus.LegacyAiUnknown
            : JobPostingSkillProvenanceStatus.Manual);
        set => _provenanceStatus = value;
    }

    public Guid? SourceParseDraftId { get; set; }
    public Guid? SourceParseDraftSkillId { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }

    public JobPosting JobPosting { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
    public JobPostingParseDraft? SourceParseDraft { get; set; }
    public JobPostingParseDraftSkill? SourceParseDraftSkill { get; set; }
}
