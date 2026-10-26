using System.Collections.Immutable;
using System.Text.Json;
using Api.Application.JobParsing.Engine.Validation;
using Api.Entities.Enums;
using Api.Infrastructure.JobParsing.OpenAI;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;

namespace API.Tests.JobParsing;

public sealed class ProviderExtractionPayloadBuilderTests
{
    [Fact]
    public void Build_TextOnlyResponse_ProducesStrictInternalJson()
    {
        const string description = "C# and Docker are required.";
        var response = new ProviderFacingExtractionResponse(
            ImmutableArray.Create(
                Candidate("C#", "C#", JobSkillRequirementType.Required),
                Candidate("Docker", "Docker", JobSkillRequirementType.Preferred)));
        var builder = new ProviderExtractionPayloadBuilder();

        var json = builder.Build(response, description);

        var validated = new ProviderResponseValidator().Validate(json, description);
        Assert.Equal(2, validated.Length);
        Assert.Equal(0, validated[0].Evidence[0].Start);
        Assert.Equal(7, validated[1].Evidence[0].Start);
    }

    [Fact]
    public void Build_UsesExpectedCaseSensitiveWirePropertyNames()
    {
        var builder = new ProviderExtractionPayloadBuilder();
        var json = builder.Build(
            new ProviderFacingExtractionResponse(
                ImmutableArray.Create(Candidate(
                    "C#",
                    "C#",
                    JobSkillRequirementType.Mentioned))),
            "C#");

        using var document = JsonDocument.Parse(json);
        var skill = document.RootElement.GetProperty("skills")[0];
        Assert.Equal("mentioned", skill.GetProperty("requirement").GetString());
        var evidence = skill.GetProperty("evidence")[0];
        Assert.Equal(0, evidence.GetProperty("start").GetInt32());
        Assert.Equal(2, evidence.GetProperty("length").GetInt32());
    }

    [Fact]
    public void Build_OutputRemainsWithinInternalStrictContract()
    {
        var json = new ProviderExtractionPayloadBuilder().Build(
            new ProviderFacingExtractionResponse(
                ImmutableArray.Create(Candidate(
                    "ASP.NET Core",
                    "ASP.NET Core",
                    JobSkillRequirementType.Required))),
            "ASP.NET Core is required.");

        var result = new ProviderJsonSchemaValidator().Validate(json);
        Assert.Single(result);
    }

    private static ProviderFacingSkillCandidate Candidate(
        string name,
        string evidence,
        JobSkillRequirementType requirementType) =>
        new(
            name,
            requirementType,
            0.9m,
            ImmutableArray.Create(new ProviderFacingEvidence(evidence)));
}
