using Api.Entities.Enums;

namespace Api.Entities;

public class JobPostingParseDraftSkill : BaseEntity
{
    public Guid JobPostingParseDraftId { get; set; }

    public string CandidateName { get; set; } = string.Empty;
    public string CanonicalKey { get; set; } = string.Empty;
    public Guid? ResolvedSkillId { get; set; }

    public JobSkillRequirementType RequirementType { get; set; }
    public int ProposedImportanceScore { get; set; }
    public decimal? Confidence { get; set; }
    public int SortOrder { get; set; }
    public SkillResolutionStatus ResolutionStatus { get; set; }

    public JobPostingParseDraft JobPostingParseDraft { get; set; } = null!;
    public Skill? ResolvedSkill { get; set; }

    public ICollection<JobPostingParseDraftSkillEvidence> Evidence { get; set; } =
        new List<JobPostingParseDraftSkillEvidence>();
}
