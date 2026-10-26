using System.Collections.Immutable;

namespace Api.Application.JobParsing.Models;

public sealed record JobDescriptionParseResult
{
    public JobDescriptionParseResult(
        string parserVersion,
        string sourceDescriptionHash,
        uint sourceJobPostingVersion,
        ImmutableArray<ParsedSkillCandidate> skills,
        ImmutableArray<string> validationWarnings,
        JobDescriptionProviderMetadata providerMetadata)
    {
        if (string.IsNullOrWhiteSpace(parserVersion))
        {
            throw new ArgumentException(
                "Parser version is required.",
                nameof(parserVersion));
        }

        if (string.IsNullOrWhiteSpace(sourceDescriptionHash))
        {
            throw new ArgumentException(
                "Source description hash is required.",
                nameof(sourceDescriptionHash));
        }

        if (sourceJobPostingVersion == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceJobPostingVersion),
                "Source job posting version must be greater than zero.");
        }

        Skills = skills.IsDefault
            ? ImmutableArray<ParsedSkillCandidate>.Empty
            : skills;
        ValidationWarnings = validationWarnings.IsDefault
            ? ImmutableArray<string>.Empty
            : validationWarnings;

        if (Skills.Any(skill => skill is null))
        {
            throw new ArgumentException(
                "Parsed skills cannot contain null values.",
                nameof(skills));
        }

        if (ValidationWarnings.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Validation warnings cannot contain empty values.",
                nameof(validationWarnings));
        }

        ArgumentNullException.ThrowIfNull(providerMetadata);

        ParserVersion = parserVersion;
        SourceDescriptionHash = sourceDescriptionHash;
        SourceJobPostingVersion = sourceJobPostingVersion;
        ProviderMetadata = providerMetadata;
    }

    public string ParserVersion { get; }
    public string SourceDescriptionHash { get; }
    public uint SourceJobPostingVersion { get; }
    public ImmutableArray<ParsedSkillCandidate> Skills { get; }
    public ImmutableArray<string> ValidationWarnings { get; }
    public JobDescriptionProviderMetadata ProviderMetadata { get; }
}
