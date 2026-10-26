using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Infrastructure.JobParsing.OpenAI.Contracts;

public sealed record ProviderFacingSkillCandidate
{
    public ProviderFacingSkillCandidate(
        string name,
        JobSkillRequirementType requirementType,
        decimal? confidence,
        ImmutableArray<ProviderFacingEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(requirementType))
        {
            throw new ArgumentOutOfRangeException(nameof(requirementType));
        }

        Name = name;
        RequirementType = requirementType;
        Confidence = confidence;
        Evidence = evidence.IsDefault
            ? ImmutableArray<ProviderFacingEvidence>.Empty
            : evidence;

        if (Evidence.Any(item => item is null))
        {
            throw new ArgumentException(
                "Evidence cannot contain null values.",
                nameof(evidence));
        }
    }

    public string Name { get; }
    public JobSkillRequirementType RequirementType { get; }
    public decimal? Confidence { get; }
    public ImmutableArray<ProviderFacingEvidence> Evidence { get; }
}
