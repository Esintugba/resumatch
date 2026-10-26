using System.Text.Json;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Entities.Enums;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;

namespace API.Tests.JobParsing;

public sealed class ProviderFacingResponseJsonValidatorTests
{
    private readonly ProviderFacingResponseJsonValidator _validator = new();

    [Fact]
    public void Validate_ValidTextOnlyContract_ReturnsImmutableModel()
    {
        const string json =
            """
            {"skills":[{"name":"C#","requirement":"required",
            "confidence":0.9,"evidence":[{"text":"C# is required"}]}]}
            """;

        var result = _validator.Validate(json);

        var skill = Assert.Single(result.Skills);
        Assert.Equal("C#", skill.Name);
        Assert.Equal(JobSkillRequirementType.Required, skill.RequirementType);
        Assert.Equal(0.9m, skill.Confidence);
        Assert.Equal("C# is required", Assert.Single(skill.Evidence).Text);
    }

    [Theory]
    [InlineData("{\"skills\":[],\"unknown\":true}")]
    [InlineData("{\"skills\":[{\"name\":\"C#\",\"requirement\":\"required\",\"confidence\":null,\"evidence\":[{\"text\":\"C#\",\"start\":0}]}]}")]
    public void Validate_UnknownProperty_IsRejected(string json)
    {
        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_DuplicateProperty_IsRejected()
    {
        const string json =
            """
            {"skills":[{"name":"C#","name":"Docker",
            "requirement":"required","confidence":null,
            "evidence":[{"text":"C#"}]}]}
            """;

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_CandidateLimit_IsEnforced()
    {
        var candidates = Enumerable.Range(
                0,
                ProviderJsonSchemaValidator.MaximumCandidateCount + 1)
            .Select(_ => Candidate())
            .ToArray();

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(JsonSerializer.Serialize(new
            {
                skills = candidates
            })));
    }

    [Fact]
    public void Validate_EvidenceLimit_IsEnforced()
    {
        var evidence = Enumerable.Range(
                0,
                ProviderJsonSchemaValidator.MaximumEvidenceCount + 1)
            .Select(_ => new { text = "C#" })
            .ToArray();
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = "C#",
                    requirement = "required",
                    confidence = (decimal?)null,
                    evidence
                }
            }
        });

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_CandidateNameLength_IsEnforced()
    {
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = new string(
                        'x',
                        ProviderJsonSchemaValidator.MaximumCandidateNameLength + 1),
                    requirement = "required",
                    confidence = 0.9m,
                    evidence = new[] { new { text = "C#" } }
                }
            }
        });

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_EvidenceTextLength_IsEnforced()
    {
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = "C#",
                    requirement = "required",
                    confidence = 0.9m,
                    evidence = new[]
                    {
                        new
                        {
                            text = new string(
                                'x',
                                ProviderJsonSchemaValidator
                                    .MaximumEvidenceTextLength + 1)
                        }
                    }
                }
            }
        });

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Validate_ConfidenceBoundaries_AreAccepted(double value)
    {
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = "C#",
                    requirement = "required",
                    confidence = value,
                    evidence = new[] { new { text = "C#" } }
                }
            }
        });

        var skill = Assert.Single(_validator.Validate(json).Skills);
        Assert.Equal((decimal)value, skill.Confidence);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_ConfidenceOutsideRange_IsRejected(double value)
    {
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = "C#",
                    requirement = "required",
                    confidence = value,
                    evidence = new[] { new { text = "C#" } }
                }
            }
        });

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_InvalidRequirement_IsRejected()
    {
        var json = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = "C#",
                    requirement = "mandatory",
                    confidence = 0.9m,
                    evidence = new[] { new { text = "C#" } }
                }
            }
        });

        AssertKind(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_ResponseOverByteLimit_IsRejected()
    {
        var json = "{\"skills\":[],\"padding\":\"" +
            new string('x', ProviderJsonSchemaValidator.MaximumJsonByteLength) +
            "\"}";

        AssertKind(
            ProviderExtractionErrorKind.InvalidProviderResponse,
            () => _validator.Validate(json));
    }

    [Fact]
    public void Validate_ResponseOverDepthLimit_IsRejected()
    {
        var json = new string('[', ProviderJsonSchemaValidator.MaximumJsonDepth + 1) +
            "0" +
            new string(']', ProviderJsonSchemaValidator.MaximumJsonDepth + 1);

        AssertKind(
            ProviderExtractionErrorKind.InvalidProviderResponse,
            () => _validator.Validate(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("```json\n{\"skills\":[]}\n```")]
    public void Validate_InvalidEnvelope_IsRejected(string json)
    {
        AssertKind(
            ProviderExtractionErrorKind.InvalidProviderResponse,
            () => _validator.Validate(json));
    }

    private static object Candidate() => new
    {
        name = "C#",
        requirement = "required",
        confidence = 0.9m,
        evidence = new[] { new { text = "C#" } }
    };

    private static void AssertKind(
        ProviderExtractionErrorKind expected,
        Action action)
    {
        var exception = Assert.Throws<ProviderExtractionException>(action);
        Assert.Equal(expected, exception.ErrorKind);
    }
}
