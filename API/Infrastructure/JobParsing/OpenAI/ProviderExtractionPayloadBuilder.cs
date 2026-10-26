using System.Text.Json;
using Api.Application.JobParsing.Engine.Validation;
using Api.Entities.Enums;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;

namespace Api.Infrastructure.JobParsing.OpenAI;

public sealed class ProviderExtractionPayloadBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly EvidenceOffsetResolver _offsetResolver;
    private readonly ProviderJsonSchemaValidator _internalValidator;

    public ProviderExtractionPayloadBuilder(
        EvidenceOffsetResolver? offsetResolver = null,
        ProviderJsonSchemaValidator? internalValidator = null)
    {
        _offsetResolver = offsetResolver ?? new EvidenceOffsetResolver();
        _internalValidator = internalValidator ?? new ProviderJsonSchemaValidator();
    }

    public string Build(
        ProviderFacingExtractionResponse response,
        string originalDescription)
    {
        var resolved = _offsetResolver.Resolve(response, originalDescription);
        var payload = new InternalPayload(resolved
            .Select(candidate => new InternalCandidate(
                candidate.CandidateName,
                ToWireValue(candidate.RequirementType),
                candidate.Confidence,
                candidate.Evidence
                    .Select(item => new InternalEvidence(
                        item.Text,
                        item.Start,
                        item.Length))
                    .ToArray()))
            .ToArray());
        var json = JsonSerializer.Serialize(payload, SerializerOptions);

        _internalValidator.Validate(json);
        return json;
    }

    private static string ToWireValue(JobSkillRequirementType requirementType) =>
        requirementType switch
        {
            JobSkillRequirementType.Required => "required",
            JobSkillRequirementType.Preferred => "preferred",
            JobSkillRequirementType.Mentioned => "mentioned",
            _ => throw new ArgumentOutOfRangeException(nameof(requirementType))
        };

    private sealed record InternalPayload(
        IReadOnlyCollection<InternalCandidate> Skills);

    private sealed record InternalCandidate(
        string Name,
        string Requirement,
        decimal? Confidence,
        IReadOnlyCollection<InternalEvidence> Evidence);

    private sealed record InternalEvidence(
        string Text,
        int Start,
        int Length);
}
