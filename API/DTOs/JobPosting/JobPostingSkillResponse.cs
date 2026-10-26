using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed class JobPostingSkillResponse
{
    public Guid SkillId { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public SkillCategory Category { get; init; }
    public JobSkillRequirementType RequirementType { get; init; }
    public int ImportanceScore { get; init; }
    public bool IsAiDetected { get; init; }
    public JobPostingSkillProvenanceStatus ProvenanceStatus { get; init; }
    public Guid? SourceParseDraftId { get; init; }
    public Guid? SourceParseDraftSkillId { get; init; }
    public DateTimeOffset? InvalidatedAt { get; init; }
}
