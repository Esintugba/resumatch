using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Entities.Enums;

namespace Api.Infrastructure.JobParsing.OpenAI.Contracts;

public sealed class ProviderFacingResponseJsonValidator
{
    private static readonly HashSet<string> RootProperties = ["skills"];
    private static readonly HashSet<string> CandidateProperties =
        ["name", "requirement", "confidence", "evidence"];
    private static readonly HashSet<string> EvidenceProperties = ["text"];

    public ProviderFacingExtractionResponse Validate(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw InvalidResponse();
        }

        if (Encoding.UTF8.GetByteCount(rawJson) >
            ProviderJsonSchemaValidator.MaximumJsonByteLength)
        {
            throw InvalidResponse();
        }

        if (rawJson.AsSpan().TrimStart().StartsWith(
                "```",
                StringComparison.Ordinal))
        {
            throw InvalidResponse();
        }

        try
        {
            using var document = JsonDocument.Parse(
                rawJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = ProviderJsonSchemaValidator.MaximumJsonDepth
                });

            return ParseRoot(document.RootElement);
        }
        catch (ProviderExtractionException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    private static ProviderFacingExtractionResponse ParseRoot(JsonElement root)
    {
        var properties = ReadStrictObject(root, RootProperties);
        var skillsElement = GetRequiredProperty(properties, "skills");
        if (skillsElement.ValueKind != JsonValueKind.Array ||
            skillsElement.GetArrayLength() >
                ProviderJsonSchemaValidator.MaximumCandidateCount)
        {
            throw SchemaViolation();
        }

        var skills = ImmutableArray.CreateBuilder<ProviderFacingSkillCandidate>(
            skillsElement.GetArrayLength());
        foreach (var item in skillsElement.EnumerateArray())
        {
            skills.Add(ParseCandidate(item));
        }

        return new ProviderFacingExtractionResponse(skills.MoveToImmutable());
    }

    private static ProviderFacingSkillCandidate ParseCandidate(JsonElement element)
    {
        var properties = ReadStrictObject(element, CandidateProperties);
        var name = GetRequiredString(
            properties,
            "name",
            ProviderJsonSchemaValidator.MaximumCandidateNameLength);
        var requirement = ParseRequirement(GetRequiredString(
            properties,
            "requirement",
            20));
        var confidence = ParseRequiredConfidence(properties);
        var evidenceElement = GetRequiredProperty(properties, "evidence");

        if (evidenceElement.ValueKind != JsonValueKind.Array ||
            evidenceElement.GetArrayLength() is 0 or >
                ProviderJsonSchemaValidator.MaximumEvidenceCount)
        {
            throw SchemaViolation();
        }

        var evidence = ImmutableArray.CreateBuilder<ProviderFacingEvidence>(
            evidenceElement.GetArrayLength());
        foreach (var item in evidenceElement.EnumerateArray())
        {
            var evidenceProperties = ReadStrictObject(item, EvidenceProperties);
            evidence.Add(new ProviderFacingEvidence(GetRequiredString(
                evidenceProperties,
                "text",
                ProviderJsonSchemaValidator.MaximumEvidenceTextLength)));
        }

        return new ProviderFacingSkillCandidate(
            name,
            requirement,
            confidence,
            evidence.MoveToImmutable());
    }

    private static Dictionary<string, JsonElement> ReadStrictObject(
        JsonElement element,
        IReadOnlySet<string> allowedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw SchemaViolation();
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) ||
                !properties.TryAdd(property.Name, property.Value))
            {
                throw SchemaViolation();
            }
        }

        return properties;
    }

    private static JsonElement GetRequiredProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            throw SchemaViolation();
        }

        return value;
    }

    private static string GetRequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        int maximumLength)
    {
        var element = GetRequiredProperty(properties, name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw SchemaViolation();
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw SchemaViolation();
        }

        return value;
    }

    private static decimal? ParseRequiredConfidence(
        IReadOnlyDictionary<string, JsonElement> properties)
    {
        var element = GetRequiredProperty(properties, "confidence");
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDecimal(out var confidence) ||
            confidence is < 0m or > 1m)
        {
            throw SchemaViolation();
        }

        return confidence;
    }

    private static JobSkillRequirementType ParseRequirement(string value) =>
        value switch
        {
            "required" => JobSkillRequirementType.Required,
            "preferred" => JobSkillRequirementType.Preferred,
            "mentioned" => JobSkillRequirementType.Mentioned,
            _ => throw SchemaViolation()
        };

    private static ProviderExtractionException InvalidResponse() =>
        new(ProviderExtractionErrorKind.InvalidProviderResponse);

    private static ProviderExtractionException SchemaViolation() =>
        new(ProviderExtractionErrorKind.SchemaValidationFailed);
}
