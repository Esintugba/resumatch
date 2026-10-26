using System.Collections.Immutable;

namespace Api.Infrastructure.JobParsing.OpenAI.Contracts;

public sealed record ProviderFacingExtractionResponse
{
    public ProviderFacingExtractionResponse(
        ImmutableArray<ProviderFacingSkillCandidate> skills)
    {
        Skills = skills.IsDefault
            ? ImmutableArray<ProviderFacingSkillCandidate>.Empty
            : skills;

        if (Skills.Any(item => item is null))
        {
            throw new ArgumentException(
                "Skills cannot contain null values.",
                nameof(skills));
        }
    }

    public ImmutableArray<ProviderFacingSkillCandidate> Skills { get; }
}
