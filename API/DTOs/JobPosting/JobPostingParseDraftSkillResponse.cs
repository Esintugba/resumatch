using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed record JobPostingParseDraftSkillResponse(
    Guid DraftSkillId,
    string CandidateName,
    string CanonicalKey,
    Guid? ResolvedSkillId,
    JobSkillRequirementType RequirementType,
    int ProposedImportanceScore,
    decimal? Confidence,
    int SortOrder,
    SkillResolutionStatus ResolutionStatus,
    IReadOnlyList<JobPostingParseDraftSkillEvidenceResponse> Evidence);
