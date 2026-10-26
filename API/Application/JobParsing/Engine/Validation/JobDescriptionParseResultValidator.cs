using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Engine.Validation;

public sealed class JobDescriptionParseResultValidator
{
    private const int MaximumCanonicalKeyLength = 150;
    private const int MaximumProviderRequestIdLength = 200;
    private const int MaximumProviderNameLength = 100;
    private const int MaximumProviderModelLength = 200;

    private readonly EvidenceValidator _evidenceValidator;

    public JobDescriptionParseResultValidator(
        EvidenceValidator? evidenceValidator = null)
    {
        _evidenceValidator = evidenceValidator ?? new EvidenceValidator();
    }

    public void Validate(
        JobDescriptionParseResult result,
        JobDescriptionParseInput input,
        string expectedParserVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(input);

        if (!string.Equals(
                result.ParserVersion,
                expectedParserVersion,
                StringComparison.Ordinal))
        {
            throw Error("parser_version_mismatch");
        }

        if (!string.Equals(
                result.SourceDescriptionHash,
                input.DescriptionHash,
                StringComparison.Ordinal) ||
            result.SourceJobPostingVersion != input.SourceJobPostingVersion)
        {
            throw Error("description_hash_mismatch");
        }

        if (result.Skills.Length > ProviderJsonSchemaValidator.MaximumCandidateCount)
        {
            throw Error("too_many_skill_candidates");
        }

        var catalog = input.SkillCatalog.ToDictionary(skill => skill.Id);
        var canonicalKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < result.Skills.Length; index++)
        {
            var candidate = result.Skills[index];
            if (candidate.SortOrder != index)
            {
                throw Error("invalid_skill_sort_order");
            }

            if (candidate.CandidateName.Length >
                    ProviderJsonSchemaValidator.MaximumCandidateNameLength ||
                string.IsNullOrWhiteSpace(candidate.CanonicalKey) ||
                candidate.CanonicalKey.Length > MaximumCanonicalKeyLength ||
                !canonicalKeys.Add(candidate.CanonicalKey))
            {
                throw Error("invalid_canonical_skill");
            }

            if (candidate.ProposedImportanceScore is < 0 or > 100 ||
                candidate.Confidence is < 0m or > 1m ||
                candidate.Evidence.Length >
                    ProviderJsonSchemaValidator.MaximumEvidenceCount)
            {
                throw Error("invalid_skill_candidate");
            }

            _evidenceValidator.ValidateAll(input.Description, candidate.Evidence);
            ValidateResolution(candidate, catalog);
        }

        var metadata = result.ProviderMetadata;
        if (metadata.RequestId.Length > MaximumProviderRequestIdLength ||
            metadata.ProviderName.Length > MaximumProviderNameLength ||
            metadata.ModelName.Length > MaximumProviderModelLength)
        {
            throw Error("invalid_provider_metadata");
        }
    }

    private static void ValidateResolution(
        ParsedSkillCandidate candidate,
        IReadOnlyDictionary<Guid, SkillCatalogItem> catalog)
    {
        var isResolved = candidate.ResolutionStatus is
            SkillResolutionStatus.Exact or SkillResolutionStatus.Alias;
        if (!isResolved)
        {
            if (candidate.ResolvedSkillId.HasValue)
            {
                throw Error("invalid_skill_resolution");
            }

            return;
        }

        if (!candidate.ResolvedSkillId.HasValue ||
            !catalog.TryGetValue(candidate.ResolvedSkillId.Value, out var skill) ||
            !string.Equals(
                candidate.ResolvedSkillName,
                skill.Name,
                StringComparison.Ordinal) ||
            candidate.ResolvedCategory != skill.Category)
        {
            throw Error("invalid_skill_resolution");
        }
    }

    private static ParserValidationException Error(string code) =>
        new(code, "The parsed job description result is invalid.");
}
