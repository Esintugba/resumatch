using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Engine.Validation;

public sealed class ProviderJsonSchemaValidator
{
    public const int MaximumJsonByteLength = 262_144;
    public const int MaximumCandidateCount = 100;
    public const int MaximumCandidateNameLength = 150;
    public const int MaximumEvidenceCount = 10;
    public const int MaximumEvidenceTextLength = 1_000;
    public const int MaximumJsonDepth = 8;

    private static readonly HashSet<string> RootProperties =
        ["skills"];
    private static readonly HashSet<string> CandidateProperties =
        ["name", "requirement", "confidence", "evidence"];
    private static readonly HashSet<string> EvidenceProperties =
        ["text", "start", "length"];

    public ImmutableArray<ProviderSkillCandidate> Validate(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw Error(
                "empty_provider_response",
                "Provider response is empty.");
        }

        if (Encoding.UTF8.GetByteCount(rawJson) > MaximumJsonByteLength)
        {
            throw Error(
                "provider_response_too_large",
                "Provider response exceeds the allowed size.");
        }

        if (rawJson.AsSpan().TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            throw Error(
                "markdown_response_not_allowed",
                "Provider response must be plain JSON without Markdown fences.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                rawJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumJsonDepth
                });

            return ParseRoot(document.RootElement);
        }
        catch (ParserValidationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Error(
                "invalid_provider_json",
                "Provider response is not valid JSON.");
        }
    }

    private static ImmutableArray<ProviderSkillCandidate> ParseRoot(
        JsonElement root)
    {
        var properties = ReadStrictObject(
            root,
            RootProperties,
            "provider response");
        var skillsElement = GetRequiredProperty(properties, "skills");

        if (skillsElement.ValueKind != JsonValueKind.Array)
        {
            throw Error(
                "invalid_skills_collection",
                "Provider skills must be a JSON array.");
        }

        if (skillsElement.GetArrayLength() > MaximumCandidateCount)
        {
            throw Error(
                "too_many_skill_candidates",
                $"Provider response cannot contain more than {MaximumCandidateCount} skills.");
        }

        var candidates = ImmutableArray.CreateBuilder<ProviderSkillCandidate>();
        foreach (var candidateElement in skillsElement.EnumerateArray())
        {
            candidates.Add(ParseCandidate(candidateElement));
        }

        return candidates.ToImmutable();
    }

    private static ProviderSkillCandidate ParseCandidate(JsonElement element)
    {
        var properties = ReadStrictObject(
            element,
            CandidateProperties,
            "skill candidate");
        var name = GetRequiredString(properties, "name", MaximumCandidateNameLength);
        var requirement = ParseRequirement(
            GetRequiredString(properties, "requirement", 20));
        var confidence = ParseConfidence(properties);
        var evidenceElement = GetRequiredProperty(properties, "evidence");

        if (evidenceElement.ValueKind != JsonValueKind.Array)
        {
            throw Error(
                "invalid_evidence_collection",
                "Candidate evidence must be a JSON array.");
        }

        if (evidenceElement.GetArrayLength() > MaximumEvidenceCount)
        {
            throw Error(
                "too_many_evidence_items",
                $"A candidate cannot contain more than {MaximumEvidenceCount} evidence items.");
        }

        var evidence = ImmutableArray.CreateBuilder<SkillEvidence>();
        foreach (var item in evidenceElement.EnumerateArray())
        {
            evidence.Add(ParseEvidence(item));
        }

        return new ProviderSkillCandidate(
            name,
            requirement,
            confidence,
            evidence.ToImmutable());
    }

    private static SkillEvidence ParseEvidence(JsonElement element)
    {
        var properties = ReadStrictObject(
            element,
            EvidenceProperties,
            "skill evidence");
        var text = GetRequiredString(
            properties,
            "text",
            MaximumEvidenceTextLength);
        var start = GetRequiredInteger(properties, "start");
        var length = GetRequiredInteger(properties, "length");

        if (start < 0)
        {
            throw Error(
                "invalid_evidence_start",
                "Evidence start cannot be negative.");
        }

        if (length <= 0 || length > MaximumEvidenceTextLength)
        {
            throw Error(
                "invalid_evidence_length",
                $"Evidence length must be between 1 and {MaximumEvidenceTextLength}.");
        }

        if (text.Length != length)
        {
            throw Error(
                "invalid_evidence_length",
                "Evidence length must equal its UTF-16 text length.");
        }

        return new SkillEvidence(text, start, length);
    }

    private static Dictionary<string, JsonElement> ReadStrictObject(
        JsonElement element,
        IReadOnlySet<string> allowedProperties,
        string objectName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Error(
                "invalid_provider_schema",
                $"The {objectName} must be a JSON object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                throw Error(
                    "unknown_provider_property",
                    $"The {objectName} contains an unsupported property.");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Error(
                    "duplicate_provider_property",
                    $"The {objectName} contains a duplicate property.");
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
            throw Error(
                "missing_provider_property",
                "Provider response is missing a required property.");
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
            throw Error(
                "invalid_provider_property_type",
                "A provider response property has an invalid type.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw Error(
                "invalid_provider_string",
                "A provider response string is empty or exceeds its allowed length.");
        }

        return value;
    }

    private static int GetRequiredInteger(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        var element = GetRequiredProperty(properties, name);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw Error(
                "invalid_provider_integer",
                "A provider response integer is invalid.");
        }

        return value;
    }

    private static decimal? ParseConfidence(
        IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("confidence", out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDecimal(out var confidence) ||
            confidence is < 0m or > 1m)
        {
            throw Error(
                "invalid_confidence",
                "Provider confidence must be null or between 0 and 1.");
        }

        return confidence;
    }

    private static JobSkillRequirementType ParseRequirement(string value)
    {
        return value switch
        {
            "required" => JobSkillRequirementType.Required,
            "preferred" => JobSkillRequirementType.Preferred,
            "mentioned" => JobSkillRequirementType.Mentioned,
            _ => throw Error(
                "invalid_requirement_type",
                "Provider requirement type is invalid.")
        };
    }

    private static ParserValidationException Error(string code, string message)
    {
        return new ParserValidationException(code, message);
    }
}
