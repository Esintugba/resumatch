using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Models;

public sealed record ParsedSkillCandidate
{
    public ParsedSkillCandidate(
        int sortOrder,
        string candidateName,
        string canonicalKey,
        Guid? resolvedSkillId,
        string? resolvedSkillName,
        SkillCategory? resolvedCategory,
        JobSkillRequirementType requirementType,
        int proposedImportanceScore,
        decimal? confidence,
        ImmutableArray<SkillEvidence> evidence,
        SkillResolutionStatus resolutionStatus)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                "Sort order cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(candidateName))
        {
            throw new ArgumentException(
                "Candidate skill name is required.",
                nameof(candidateName));
        }

        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            throw new ArgumentException(
                "Canonical skill key is required.",
                nameof(canonicalKey));
        }

        if (!Enum.IsDefined(requirementType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Skill requirement type is not defined.");
        }

        if (proposedImportanceScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposedImportanceScore),
                "Proposed importance score must be between 0 and 100.");
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be between 0 and 1 when provided.");
        }

        if (!Enum.IsDefined(resolutionStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolutionStatus),
                "Skill resolution status is not defined.");
        }

        ValidateResolution(
            resolutionStatus,
            resolvedSkillId,
            resolvedSkillName,
            resolvedCategory);

        Evidence = evidence.IsDefault
            ? ImmutableArray<SkillEvidence>.Empty
            : evidence;

        if (Evidence.IsEmpty || Evidence.Any(item => item is null))
        {
            throw new ArgumentException(
                "A parsed skill candidate requires non-null evidence.",
                nameof(evidence));
        }

        SortOrder = sortOrder;
        CandidateName = candidateName;
        CanonicalKey = canonicalKey;
        ResolvedSkillId = resolvedSkillId;
        ResolvedSkillName = resolvedSkillName;
        ResolvedCategory = resolvedCategory;
        RequirementType = requirementType;
        ProposedImportanceScore = proposedImportanceScore;
        Confidence = confidence;
        ResolutionStatus = resolutionStatus;
    }

    public int SortOrder { get; }
    public string CandidateName { get; }
    public string CanonicalKey { get; }
    public Guid? ResolvedSkillId { get; }
    public string? ResolvedSkillName { get; }
    public SkillCategory? ResolvedCategory { get; }
    public JobSkillRequirementType RequirementType { get; }
    public int ProposedImportanceScore { get; }
    public decimal? Confidence { get; }
    public ImmutableArray<SkillEvidence> Evidence { get; }
    public SkillResolutionStatus ResolutionStatus { get; }

    private static void ValidateResolution(
        SkillResolutionStatus status,
        Guid? skillId,
        string? skillName,
        SkillCategory? category)
    {
        var isResolved = status is
            SkillResolutionStatus.Exact or SkillResolutionStatus.Alias;

        if (isResolved)
        {
            if (!skillId.HasValue || skillId.Value == Guid.Empty)
            {
                throw new ArgumentException(
                    "A resolved candidate requires a non-empty skill identifier.",
                    nameof(skillId));
            }

            if (string.IsNullOrWhiteSpace(skillName))
            {
                throw new ArgumentException(
                    "A resolved candidate requires a skill name.",
                    nameof(skillName));
            }

            if (!category.HasValue || !Enum.IsDefined(category.Value))
            {
                throw new ArgumentException(
                    "A resolved candidate requires a valid skill category.",
                    nameof(category));
            }

            return;
        }

        if (skillId.HasValue || skillName is not null || category.HasValue)
        {
            throw new ArgumentException(
                "Ambiguous or unresolved candidates cannot select a resolved skill.",
                nameof(skillId));
        }
    }
}
