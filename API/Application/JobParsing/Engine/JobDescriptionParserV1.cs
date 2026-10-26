using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing.Engine;

public sealed class JobDescriptionParserV1 : IJobDescriptionParser
{
    private readonly IJobDescriptionExtractionProvider _extractionProvider;
    private readonly ISkillCanonicalizer _skillCanonicalizer;
    private readonly JobDescriptionPreprocessor _preprocessor;
    private readonly ProviderResponseValidator _responseValidator;
    private readonly ParsedSkillDuplicateResolverV1 _duplicateResolver;
    private readonly JobDescriptionParseResultValidator _resultValidator;

    public JobDescriptionParserV1(
        IJobDescriptionExtractionProvider extractionProvider,
        ISkillCanonicalizer skillCanonicalizer,
        JobDescriptionPreprocessor preprocessor,
        ProviderResponseValidator responseValidator,
        ParsedSkillDuplicateResolverV1 duplicateResolver,
        JobDescriptionParseResultValidator? resultValidator = null)
    {
        _extractionProvider = extractionProvider ??
            throw new ArgumentNullException(nameof(extractionProvider));
        _skillCanonicalizer = skillCanonicalizer ??
            throw new ArgumentNullException(nameof(skillCanonicalizer));
        _preprocessor = preprocessor ??
            throw new ArgumentNullException(nameof(preprocessor));
        _responseValidator = responseValidator ??
            throw new ArgumentNullException(nameof(responseValidator));
        _duplicateResolver = duplicateResolver ??
            throw new ArgumentNullException(nameof(duplicateResolver));
        _resultValidator = resultValidator ??
            new JobDescriptionParseResultValidator();
    }

    public string ParserVersion => JobParserVersions.V1;

    public async Task<JobDescriptionParseResult> ParseAsync(
        JobDescriptionParseInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var preprocessed = _preprocessor.Process(input.Description);
        if (!string.Equals(
                input.DescriptionHash,
                preprocessed.DescriptionHash,
                StringComparison.Ordinal))
        {
            throw new ParserValidationException(
                "description_hash_mismatch",
                "The job description has changed since the parse input was created.");
        }

        var providerRequest = new ProviderExtractionRequest(
            input.Title,
            preprocessed.OriginalDescription,
            ParserVersion);
        var providerResult = await _extractionProvider.ExtractAsync(
            providerRequest,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (providerResult is null)
        {
            throw new ParserValidationException(
                "missing_provider_response",
                "The extraction provider did not return a response.");
        }

        var extractedCandidates = _responseValidator.Validate(
            providerResult.RawJsonPayload,
            preprocessed.OriginalDescription);
        var parsedCandidates = extractedCandidates
            .Select((candidate, index) => MapCandidate(
                candidate,
                index,
                input.SkillCatalog))
            .ToImmutableArray();
        var resolvedCandidates = _duplicateResolver.Resolve(parsedCandidates);

        var result = new JobDescriptionParseResult(
            ParserVersion,
            preprocessed.DescriptionHash,
            input.SourceJobPostingVersion,
            resolvedCandidates,
            ImmutableArray<string>.Empty,
            new JobDescriptionProviderMetadata(
                providerResult.ProviderRequestId,
                providerResult.ProviderName,
                providerResult.ModelName,
                providerResult.Usage));
        _resultValidator.Validate(result, input, ParserVersion);
        return result;
    }

    private ParsedSkillCandidate MapCandidate(
        ExtractedSkillCandidate candidate,
        int sourceOrder,
        IReadOnlyCollection<SkillCatalogItem> skillCatalog)
    {
        var resolution = _skillCanonicalizer.Resolve(candidate, skillCatalog);
        var resolvedSkill = resolution.ResolvedSkill;

        return new ParsedSkillCandidate(
            sourceOrder,
            candidate.CandidateName,
            resolution.CanonicalKey,
            resolvedSkill?.Id,
            resolvedSkill?.Name,
            resolvedSkill?.Category,
            candidate.RequirementType,
            RequirementImportancePolicy.GetProposedScore(
                candidate.RequirementType),
            candidate.Confidence,
            candidate.Evidence,
            resolution.Status);
    }

}
