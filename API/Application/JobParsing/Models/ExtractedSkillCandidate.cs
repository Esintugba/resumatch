using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Models;

public sealed record ExtractedSkillCandidate
{
    public ExtractedSkillCandidate(
        string candidateName,
        string normalizedCandidate,
        JobSkillRequirementType requirementType,
        decimal? confidence,
        ImmutableArray<SkillEvidence> evidence)
    {
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            throw new ArgumentException(
                "Candidate skill name is required.",
                nameof(candidateName));
        }

        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            throw new ArgumentException(
                "Normalized candidate skill name is required.",
                nameof(normalizedCandidate));
        }

        if (!Enum.IsDefined(requirementType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Skill requirement type is not defined.");
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be between 0 and 1 when provided.");
        }

        Evidence = evidence.IsDefault
            ? ImmutableArray<SkillEvidence>.Empty
            : evidence;

        if (Evidence.IsEmpty || Evidence.Any(item => item is null))
        {
            throw new ArgumentException(
                "A validated skill candidate requires non-null evidence.",
                nameof(evidence));
        }

        CandidateName = candidateName;
        NormalizedCandidate = normalizedCandidate;
        RequirementType = requirementType;
        Confidence = confidence;
    }

    public string CandidateName { get; }
    public string NormalizedCandidate { get; }
    public JobSkillRequirementType RequirementType { get; }
    public decimal? Confidence { get; }
    public ImmutableArray<SkillEvidence> Evidence { get; }
}
