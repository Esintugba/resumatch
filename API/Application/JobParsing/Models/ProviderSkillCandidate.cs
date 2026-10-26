using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Models;

public sealed record ProviderSkillCandidate
{
    public ProviderSkillCandidate(
        string candidateName,
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

        if (!Enum.IsDefined(requirementType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Skill requirement type is not defined.");
        }

        ValidateConfidence(confidence);

        Evidence = evidence.IsDefault
            ? ImmutableArray<SkillEvidence>.Empty
            : evidence;

        if (Evidence.Any(item => item is null))
        {
            throw new ArgumentException(
                "Evidence cannot contain null values.",
                nameof(evidence));
        }

        CandidateName = candidateName;
        RequirementType = requirementType;
        Confidence = confidence;
    }

    public string CandidateName { get; }
    public JobSkillRequirementType RequirementType { get; }
    public decimal? Confidence { get; }
    public ImmutableArray<SkillEvidence> Evidence { get; }

    private static void ValidateConfidence(decimal? confidence)
    {
        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be between 0 and 1 when provided.");
        }
    }
}
