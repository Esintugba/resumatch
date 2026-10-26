using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Application.JobParsing.Models;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;

namespace Api.Infrastructure.JobParsing.OpenAI;

public sealed class EvidenceOffsetResolver
{
    private readonly EvidenceValidator _evidenceValidator;

    public EvidenceOffsetResolver(EvidenceValidator? evidenceValidator = null)
    {
        _evidenceValidator = evidenceValidator ?? new EvidenceValidator();
    }

    public ImmutableArray<ProviderSkillCandidate> Resolve(
        ProviderFacingExtractionResponse response,
        string originalDescription)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(originalDescription);

        var resolved = ImmutableArray.CreateBuilder<ProviderSkillCandidate>(
            response.Skills.Length);
        foreach (var candidate in response.Skills)
        {
            if (candidate.Evidence.IsDefaultOrEmpty)
            {
                throw SchemaViolation();
            }

            var usedOccurrences = new Dictionary<string, HashSet<int>>(
                StringComparer.Ordinal);
            var evidence = ImmutableArray.CreateBuilder<SkillEvidence>(
                candidate.Evidence.Length);

            foreach (var item in candidate.Evidence)
            {
                if (string.IsNullOrWhiteSpace(item.Text) ||
                    item.Text.Length >
                        ProviderJsonSchemaValidator.MaximumEvidenceTextLength)
                {
                    throw SchemaViolation();
                }

                if (!usedOccurrences.TryGetValue(item.Text, out var usedStarts))
                {
                    usedStarts = [];
                    usedOccurrences.Add(item.Text, usedStarts);
                }

                var start = FindFirstUnusedOccurrence(
                    originalDescription,
                    item.Text,
                    usedStarts);
                if (start < 0)
                {
                    throw SchemaViolation();
                }

                usedStarts.Add(start);
                var resolvedEvidence = new SkillEvidence(
                    item.Text,
                    start,
                    item.Text.Length);
                _evidenceValidator.Validate(originalDescription, resolvedEvidence);
                evidence.Add(resolvedEvidence);
            }

            resolved.Add(new ProviderSkillCandidate(
                candidate.Name,
                candidate.RequirementType,
                candidate.Confidence,
                evidence.MoveToImmutable()));
        }

        return resolved.MoveToImmutable();
    }

    private static int FindFirstUnusedOccurrence(
        string source,
        string value,
        IReadOnlySet<int> usedStarts)
    {
        var searchStart = 0;
        while (searchStart <= source.Length - value.Length)
        {
            var match = source.IndexOf(
                value,
                searchStart,
                StringComparison.Ordinal);
            if (match < 0)
            {
                return -1;
            }

            if (!usedStarts.Contains(match))
            {
                return match;
            }

            searchStart = match + 1;
        }

        return -1;
    }

    private static ProviderExtractionException SchemaViolation() =>
        new(ProviderExtractionErrorKind.SchemaValidationFailed);
}
