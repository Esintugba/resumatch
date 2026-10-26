using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing.Engine.Validation;

public sealed class ProviderResponseValidator
{
    private readonly ProviderJsonSchemaValidator _schemaValidator;
    private readonly EvidenceValidator _evidenceValidator;

    public ProviderResponseValidator(
        ProviderJsonSchemaValidator? schemaValidator = null,
        EvidenceValidator? evidenceValidator = null)
    {
        _schemaValidator = schemaValidator ?? new ProviderJsonSchemaValidator();
        _evidenceValidator = evidenceValidator ?? new EvidenceValidator();
    }

    public ImmutableArray<ExtractedSkillCandidate> Validate(
        string? rawProviderJson,
        string originalDescription)
    {
        ArgumentNullException.ThrowIfNull(originalDescription);

        var providerCandidates = _schemaValidator.Validate(rawProviderJson);
        var validated = ImmutableArray.CreateBuilder<ExtractedSkillCandidate>(
            providerCandidates.Length);

        foreach (var candidate in providerCandidates)
        {
            _evidenceValidator.ValidateAll(
                originalDescription,
                candidate.Evidence);

            var normalizedCandidate =
                JobDescriptionPreprocessor.NormalizeForMatching(
                    candidate.CandidateName);

            validated.Add(new ExtractedSkillCandidate(
                candidate.CandidateName,
                normalizedCandidate,
                candidate.RequirementType,
                candidate.Confidence,
                candidate.Evidence));
        }

        return validated.MoveToImmutable();
    }
}
