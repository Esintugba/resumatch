using System.Collections.Immutable;
using System.Text.Json;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class JobDescriptionParserV1Tests
{
    private const string Description =
        "C# and ASP.NET Core are required. PostgreSQL is preferred. " +
        "Azure and Docker are mentioned. postgres is also preferred.";

    [Fact]
    public async Task ParseAsync_ValidPayload_ReturnsValidatedImmutableResult()
    {
        var (parser, provider) = CreateParser(Payload(
            Candidate("C#", "required", 0.91m)));

        var result = await parser.ParseAsync(Input(), CancellationToken.None);

        var skill = Assert.Single(result.Skills);
        Assert.Equal("c#", skill.CanonicalKey);
        Assert.Equal(SkillResolutionStatus.Exact, skill.ResolutionStatus);
        Assert.Equal(1, provider.CallCount);
        Assert.Empty(result.ValidationWarnings);
    }

    [Fact]
    public async Task ParseAsync_UsesPublishedV1ParserVersion()
    {
        var (parser, _) = CreateParser(EmptyPayload());

        var result = await parser.ParseAsync(Input(), CancellationToken.None);

        Assert.Equal("job-parser-v1", parser.ParserVersion);
        Assert.Equal(JobParserVersions.V1, result.ParserVersion);
    }

    [Fact]
    public async Task ParseAsync_SendsOriginalDescriptionWithoutCatalogToProvider()
    {
        var (parser, provider) = CreateParser(EmptyPayload());
        var input = Input();

        await parser.ParseAsync(input, CancellationToken.None);

        Assert.NotNull(provider.LastRequest);
        Assert.Equal(input.Description, provider.LastRequest.Description);
        Assert.Equal(input.Title, provider.LastRequest.Title);
        Assert.Equal(JobParserVersions.V1, provider.LastRequest.ParserVersion);
        Assert.Equal(
            ["Description", "ParserVersion", "Title"],
            typeof(ProviderExtractionRequest).GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ParseAsync_HashMismatch_IsRejectedBeforeProviderCall()
    {
        var (parser, provider) = CreateParser(EmptyPayload());
        var input = Input(descriptionHash: new string('0', 64));

        var exception = await Assert.ThrowsAsync<ParserValidationException>(
            () => parser.ParseAsync(input, CancellationToken.None));

        Assert.Equal("description_hash_mismatch", exception.Code);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ParseAsync_SameInputAndPayload_ReturnsSameLogicalOutput()
    {
        var payload = Payload(
            Candidate("Docker", "mentioned", 0.99m),
            Candidate("C#", "required", 0.72m));
        var (parser, _) = CreateParser(payload);
        var input = Input();

        var first = await parser.ParseAsync(input, CancellationToken.None);
        var second = await parser.ParseAsync(input, CancellationToken.None);

        Assert.Equal(Signatures(first), Signatures(second));
        Assert.Equal(first.SourceDescriptionHash, second.SourceDescriptionHash);
    }

    [Fact]
    public async Task ParseAsync_ExactCatalogMatch_SelectsSkill()
    {
        var (parser, _) = CreateParser(Payload(Candidate("C#", "required")));

        var skill = Assert.Single((await parser.ParseAsync(
            Input(), CancellationToken.None)).Skills);

        Assert.Equal(Id(1), skill.ResolvedSkillId);
        Assert.Equal("C#", skill.ResolvedSkillName);
        Assert.Equal(SkillResolutionStatus.Exact, skill.ResolutionStatus);
    }

    [Fact]
    public async Task ParseAsync_AliasCatalogMatch_SelectsCanonicalSkill()
    {
        var (parser, _) = CreateParser(Payload(Candidate("postgres", "preferred")));

        var skill = Assert.Single((await parser.ParseAsync(
            Input(), CancellationToken.None)).Skills);

        Assert.Equal(Id(3), skill.ResolvedSkillId);
        Assert.Equal("PostgreSQL", skill.ResolvedSkillName);
        Assert.Equal("postgresql", skill.CanonicalKey);
        Assert.Equal(SkillResolutionStatus.Alias, skill.ResolutionStatus);
    }

    [Fact]
    public async Task ParseAsync_AmbiguousCatalogMatch_DoesNotSelectSkillId()
    {
        var catalog = Catalog().Add(new SkillCatalogItem(
            Id(10), "Azure Platform", "azure", SkillCategory.Tool));
        var (parser, _) = CreateParser(Payload(Candidate("Azure", "mentioned")));

        var skill = Assert.Single((await parser.ParseAsync(
            Input(skillCatalog: catalog), CancellationToken.None)).Skills);

        Assert.Equal(SkillResolutionStatus.Ambiguous, skill.ResolutionStatus);
        Assert.Null(skill.ResolvedSkillId);
        Assert.Null(skill.ResolvedSkillName);
    }

    [Fact]
    public async Task ParseAsync_UnresolvedCandidate_DoesNotSelectSkillId()
    {
        var (parser, _) = CreateParser(Payload(Candidate("Docker", "mentioned")));

        var skill = Assert.Single((await parser.ParseAsync(
            Input(skillCatalog: Catalog().Where(item => item.Name != "Docker")
                .ToImmutableArray()), CancellationToken.None)).Skills);

        Assert.Equal(SkillResolutionStatus.Unresolved, skill.ResolutionStatus);
        Assert.Null(skill.ResolvedSkillId);
    }

    [Fact]
    public async Task ParseAsync_CanonicalDuplicate_IsReturnedOnce()
    {
        var (parser, _) = CreateParser(Payload(
            Candidate("PostgreSQL", "preferred", occurrence: 0),
            Candidate("postgres", "mentioned", occurrence: 0)));

        var result = await parser.ParseAsync(Input(), CancellationToken.None);

        Assert.Single(result.Skills);
        Assert.Equal(2, result.Skills[0].Evidence.Length);
    }

    [Fact]
    public async Task ParseAsync_RequiredDuplicate_WinsOverPreferred()
    {
        var (parser, _) = CreateParser(Payload(
            Candidate("Docker", "preferred", 0.99m),
            Candidate("Docker", "required", 0.01m)));

        var skill = Assert.Single((await parser.ParseAsync(
            Input(), CancellationToken.None)).Skills);

        Assert.Equal(JobSkillRequirementType.Required, skill.RequirementType);
        Assert.Equal(100, skill.ProposedImportanceScore);
    }

    [Fact]
    public async Task ParseAsync_PreservesValidatedEvidenceOffsets()
    {
        var candidate = Candidate("ASP.NET Core", "required");
        var (parser, _) = CreateParser(Payload(candidate));

        var evidence = Assert.Single(Assert.Single((await parser.ParseAsync(
            Input(), CancellationToken.None)).Skills).Evidence);

        Assert.Equal(candidate.Start, evidence.Start);
        Assert.Equal(candidate.Text.Length, evidence.Length);
        Assert.Equal(candidate.Text, evidence.Text);
    }

    [Fact]
    public async Task ParseAsync_AssignsContiguousZeroBasedSortOrder()
    {
        var (parser, _) = CreateParser(Payload(
            Candidate("Docker", "mentioned"),
            Candidate("C#", "required"),
            Candidate("PostgreSQL", "preferred")));

        var result = await parser.ParseAsync(Input(), CancellationToken.None);

        Assert.Equal([0, 1, 2], result.Skills.Select(skill => skill.SortOrder));
    }

    [Fact]
    public async Task ParseAsync_Confidence_DoesNotChangeServerImportancePolicy()
    {
        var (parser, _) = CreateParser(Payload(
            Candidate("C#", "required", 0m),
            Candidate("PostgreSQL", "preferred", 1m),
            Candidate("Docker", "mentioned", 1m)));

        var result = await parser.ParseAsync(Input(), CancellationToken.None);

        Assert.Equal(100, result.Skills.Single(x => x.CanonicalKey == "c#").ProposedImportanceScore);
        Assert.Equal(60, result.Skills.Single(x => x.CanonicalKey == "postgresql").ProposedImportanceScore);
        Assert.Equal(30, result.Skills.Single(x => x.CanonicalKey == "docker").ProposedImportanceScore);
    }

    [Fact]
    public async Task ParseAsync_InvalidJson_RejectsWholeParse()
    {
        var (parser, _) = CreateParser("not-json");

        var exception = await Assert.ThrowsAsync<ParserValidationException>(
            () => parser.ParseAsync(Input(), CancellationToken.None));

        Assert.Equal("invalid_provider_json", exception.Code);
        Assert.DoesNotContain("not-json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_EvidenceMismatch_RejectsWholeParse()
    {
        var mismatched = new CandidateSpec("Go", "required", 0.8m, 0, "Go");
        var (parser, _) = CreateParser(Payload(mismatched));

        var exception = await Assert.ThrowsAsync<ParserValidationException>(
            () => parser.ParseAsync(Input(), CancellationToken.None));

        Assert.Equal("evidence_text_mismatch", exception.Code);
    }

    [Fact]
    public async Task ParseAsync_ProviderException_PropagatesWithoutProducingResult()
    {
        var expected = new TimeoutException("provider unavailable");
        var (parser, provider) = CreateParser(EmptyPayload());
        provider.ExceptionToThrow = expected;

        var actual = await Assert.ThrowsAsync<TimeoutException>(
            () => parser.ParseAsync(Input(), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ParseAsync_ForwardsCancellationTokenToProvider()
    {
        var (parser, provider) = CreateParser(EmptyPayload());
        provider.SimulateCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var parseTask = parser.ParseAsync(Input(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parseTask);
        Assert.Equal(cancellation.Token, provider.LastCancellationToken);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ParseAsync_PreCanceledToken_DoesNotCallProvider()
    {
        var (parser, provider) = CreateParser(EmptyPayload());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parser.ParseAsync(Input(), cancellation.Token));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ParseAsync_ProducesContractModelsWithoutEfEntityDependencies()
    {
        var (parser, _) = CreateParser(Payload(Candidate("C#", "required")));

        var result = await parser.ParseAsync(Input(), CancellationToken.None);
        var constructorDependencies = typeof(JobDescriptionParserV1)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.IsType<JobDescriptionParseResult>(result);
        Assert.All(result.Skills, skill => Assert.IsType<ParsedSkillCandidate>(skill));
        Assert.DoesNotContain(constructorDependencies, type =>
            type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true ||
            type.Name.Contains("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParseAsync_ProviderCannotSupplySkillId()
    {
        var valid = Candidate("C#", "required");
        var payload = JsonSerializer.Serialize(new
        {
            skills = new[]
            {
                new
                {
                    name = valid.Text,
                    skillId = Id(99),
                    requirement = valid.Requirement,
                    confidence = valid.Confidence,
                    evidence = new[]
                    {
                        new { text = valid.Text, start = valid.Start, length = valid.Text.Length }
                    }
                }
            }
        });
        var (parser, _) = CreateParser(payload);

        var exception = await Assert.ThrowsAsync<ParserValidationException>(
            () => parser.ParseAsync(Input(), CancellationToken.None));

        Assert.Equal("unknown_provider_property", exception.Code);
    }

    [Fact]
    public async Task ParseAsync_ReturnsServerComputedSourceHashAndSourceVersion()
    {
        var input = Input();
        var (parser, _) = CreateParser(EmptyPayload());

        var result = await parser.ParseAsync(input, CancellationToken.None);

        Assert.Equal(DescriptionHashCalculator.Compute(Description), result.SourceDescriptionHash);
        Assert.Equal(input.SourceJobPostingVersion, result.SourceJobPostingVersion);
    }

    private static (JobDescriptionParserV1 Parser, FakeJobDescriptionExtractionProvider Provider)
        CreateParser(string payload)
    {
        var provider = new FakeJobDescriptionExtractionProvider(payload);
        var parser = new JobDescriptionParserV1(
            provider,
            new SkillCanonicalizerV1(),
            new JobDescriptionPreprocessor(),
            new ProviderResponseValidator(),
            new ParsedSkillDuplicateResolverV1());
        return (parser, provider);
    }

    private static JobDescriptionParseInput Input(
        string? descriptionHash = null,
        ImmutableArray<SkillCatalogItem> skillCatalog = default)
    {
        if (skillCatalog.IsDefault)
        {
            skillCatalog = Catalog();
        }

        return new JobDescriptionParseInput(
            Id(100),
            "Backend Engineer",
            Description,
            descriptionHash ?? DescriptionHashCalculator.Compute(Description),
            7,
            new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero),
            skillCatalog);
    }

    private static ImmutableArray<SkillCatalogItem> Catalog() =>
    [
        new(Id(1), "C#", "c#", SkillCategory.ProgrammingLanguage),
        new(Id(2), "ASP.NET Core", "asp.net core", SkillCategory.Backend),
        new(Id(3), "PostgreSQL", "postgresql", SkillCategory.Database),
        new(Id(4), "Azure", "azure", SkillCategory.Cloud),
        new(Id(5), "Docker", "docker", SkillCategory.DevOps)
    ];

    private static string EmptyPayload() => "{\"skills\":[]}";

    private static string Payload(params CandidateSpec[] candidates)
    {
        return JsonSerializer.Serialize(new
        {
            skills = candidates.Select(candidate => new
            {
                name = candidate.Name,
                requirement = candidate.Requirement,
                confidence = candidate.Confidence,
                evidence = new[]
                {
                    new
                    {
                        text = candidate.Text,
                        start = candidate.Start,
                        length = candidate.Text.Length
                    }
                }
            })
        });
    }

    private static CandidateSpec Candidate(
        string text,
        string requirement,
        decimal? confidence = 0.8m,
        int occurrence = 0)
    {
        var start = FindOccurrence(Description, text, occurrence);
        return new CandidateSpec(text, requirement, confidence, start, text);
    }

    private static int FindOccurrence(string source, string value, int occurrence)
    {
        var start = 0;
        for (var index = 0; index <= occurrence; index++)
        {
            start = source.IndexOf(value, start, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException($"Test evidence '{value}' was not found.");
            }

            if (index < occurrence)
            {
                start += value.Length;
            }
        }

        return start;
    }

    private static string[] Signatures(JobDescriptionParseResult result)
    {
        return result.Skills.Select(skill => string.Join(
            '|',
            skill.SortOrder,
            skill.CandidateName,
            skill.CanonicalKey,
            skill.ResolvedSkillId,
            skill.RequirementType,
            skill.ProposedImportanceScore,
            skill.Confidence,
            skill.ResolutionStatus,
            string.Join(',', skill.Evidence.Select(evidence =>
                $"{evidence.Start}:{evidence.Length}:{evidence.Text}"))))
            .ToArray();
    }

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private sealed record CandidateSpec(
        string Name,
        string Requirement,
        decimal? Confidence,
        int Start,
        string Text);
}

