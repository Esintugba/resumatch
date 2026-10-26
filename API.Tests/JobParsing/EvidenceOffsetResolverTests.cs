using System.Collections.Immutable;
using Api.Application.JobParsing.Errors;
using Api.Entities.Enums;
using Api.Infrastructure.JobParsing.OpenAI;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;

namespace API.Tests.JobParsing;

public sealed class EvidenceOffsetResolverTests
{
    private readonly EvidenceOffsetResolver _resolver = new();

    [Fact]
    public void Resolve_SingleEvidence_UsesExactUtf16Offset()
    {
        const string description = "Build services with C# and PostgreSQL.";

        var result = _resolver.Resolve(
            Response(Candidate("C#", "C#")),
            description);

        var evidence = Assert.Single(Assert.Single(result).Evidence);
        Assert.Equal(description.IndexOf("C#", StringComparison.Ordinal), evidence.Start);
        Assert.Equal("C#".Length, evidence.Length);
        Assert.Equal("C#", description.Substring(evidence.Start, evidence.Length));
    }

    [Fact]
    public void Resolve_RepeatedEvidence_UsesSmallestUnusedOccurrence()
    {
        const string description = "C# first; C# second; C# third.";

        var result = _resolver.Resolve(
            Response(Candidate("C#", "C#", "C#", "C#")),
            description);

        var starts = Assert.Single(result).Evidence.Select(item => item.Start).ToArray();
        Assert.Equal(new[] { 0, 10, 21 }, starts);
    }

    [Fact]
    public void Resolve_DifferentCandidates_MayReuseSameOccurrence()
    {
        const string description = "C# and .NET are required.";
        var response = Response(
            Candidate("C#", description),
            Candidate(".NET", description));

        var result = _resolver.Resolve(response, description);

        Assert.Equal(2, result.Length);
        Assert.All(result, item => Assert.Equal(0, Assert.Single(item.Evidence).Start));
    }

    [Fact]
    public void Resolve_NoUnusedOccurrence_Remains_FailsClosed()
    {
        const string description = "C# is required.";

        var exception = Assert.Throws<ProviderExtractionException>(() =>
            _resolver.Resolve(
                Response(Candidate("C#", "C#", "C#")),
                description));

        Assert.Equal(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            exception.ErrorKind);
    }

    [Fact]
    public void Resolve_EvidenceMissingFromSource_IsRejected()
    {
        var exception = Assert.Throws<ProviderExtractionException>(() =>
            _resolver.Resolve(
                Response(Candidate("Docker", "Docker")),
                "C# is required."));

        Assert.Equal(
            ProviderExtractionErrorKind.SchemaValidationFailed,
            exception.ErrorKind);
    }

    [Fact]
    public void Resolve_CaseMismatch_IsRejected()
    {
        Assert.Throws<ProviderExtractionException>(() =>
            _resolver.Resolve(
                Response(Candidate("C#", "c#")),
                "C# is required."));
    }

    [Fact]
    public void Resolve_EmojiBeforeEvidence_UsesUtf16CodeUnits()
    {
        const string description = "😀 tools then C#";

        var result = _resolver.Resolve(
            Response(Candidate("C#", "C#")),
            description);

        var evidence = Assert.Single(Assert.Single(result).Evidence);
        Assert.Equal(description.IndexOf("C#", StringComparison.Ordinal), evidence.Start);
        Assert.Equal(14, evidence.Start);
        Assert.Equal("C#", description.Substring(evidence.Start, evidence.Length));
    }

    [Fact]
    public void Resolve_EmptyEvidence_IsRejected()
    {
        Assert.Throws<ProviderExtractionException>(() =>
            _resolver.Resolve(
                Response(Candidate("C#", " ")),
                "C# is required."));
    }

    private static ProviderFacingExtractionResponse Response(
        params ProviderFacingSkillCandidate[] candidates) =>
        new(candidates.ToImmutableArray());

    private static ProviderFacingSkillCandidate Candidate(
        string name,
        params string[] evidence) =>
        new(
            name,
            JobSkillRequirementType.Required,
            0.9m,
            evidence
                .Select(item => new ProviderFacingEvidence(item))
                .ToImmutableArray());
}
