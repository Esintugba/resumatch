using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class ParsedSkillDuplicateResolverV1Tests
{
    private readonly ParsedSkillDuplicateResolverV1 _resolver = new();

    [Fact]
    public void Resolve_SameCanonicalKeyWithDifferentSkillIds_ReturnsAmbiguous()
    {
        var result = _resolver.Resolve(
        [
            Candidate("Azure", "azure", JobSkillRequirementType.Required, 100,
                skillId: Id(1), category: SkillCategory.Cloud),
            Candidate("Azure", "azure", JobSkillRequirementType.Preferred, 60,
                skillId: Id(2), category: SkillCategory.Tool)
        ]);

        var candidate = Assert.Single(result);
        Assert.Equal(SkillResolutionStatus.Ambiguous, candidate.ResolutionStatus);
        Assert.Null(candidate.ResolvedSkillId);
    }

    [Fact]
    public void Resolve_RequiredWinsOverPreferred()
    {
        var result = ResolveRequirementPair(
            JobSkillRequirementType.Required,
            40,
            JobSkillRequirementType.Preferred,
            100);

        Assert.Equal(JobSkillRequirementType.Required, result.RequirementType);
        Assert.Equal(40, result.ProposedImportanceScore);
    }

    [Fact]
    public void Resolve_PreferredWinsOverMentioned()
    {
        var result = ResolveRequirementPair(
            JobSkillRequirementType.Preferred,
            40,
            JobSkillRequirementType.Mentioned,
            100);

        Assert.Equal(JobSkillRequirementType.Preferred, result.RequirementType);
    }

    [Fact]
    public void Resolve_SameRequirementUsesHigherImportance()
    {
        var result = ResolveRequirementPair(
            JobSkillRequirementType.Preferred,
            50,
            JobSkillRequirementType.Preferred,
            80);

        Assert.Equal(80, result.ProposedImportanceScore);
    }

    [Fact]
    public void Resolve_ConfidenceDoesNotOverrideRequirementPriority()
    {
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate("Docker", "docker", JobSkillRequirementType.Required, 40,
                confidence: 0.1m),
            Candidate("docker", "docker", JobSkillRequirementType.Preferred, 100,
                confidence: 0.99m)
        ]));

        Assert.Equal(JobSkillRequirementType.Required, result.RequirementType);
        Assert.Equal(40, result.ProposedImportanceScore);
        Assert.Equal(0.99m, result.Confidence);
    }

    [Fact]
    public void Resolve_MergesEvidenceInDeterministicOffsetOrder()
    {
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate("Docker", "docker", JobSkillRequirementType.Required, 100,
                evidence: [Evidence("second", 20)]),
            Candidate("docker", "docker", JobSkillRequirementType.Required, 100,
                evidence: [Evidence("first", 5)])
        ]));

        Assert.Equal([5, 20], result.Evidence.Select(item => item.Start));
    }

    [Fact]
    public void Resolve_RemovesDuplicateEvidence()
    {
        var duplicate = Evidence("Docker", 10);

        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate("Docker", "docker", JobSkillRequirementType.Required, 100,
                evidence: [duplicate]),
            Candidate("docker", "docker", JobSkillRequirementType.Required, 100,
                evidence: [duplicate])
        ]));

        Assert.Single(result.Evidence);
    }

    [Fact]
    public void Resolve_InputOrderDoesNotChangeLogicalOutput()
    {
        var candidates = ImmutableArray.Create(
            Candidate("Docker", "docker", JobSkillRequirementType.Preferred, 60),
            Candidate("C#", "c#", JobSkillRequirementType.Required, 100),
            Candidate("docker", "docker", JobSkillRequirementType.Required, 80));

        var first = _resolver.Resolve(candidates);
        var second = _resolver.Resolve(candidates.Reverse().ToImmutableArray());

        Assert.Equal(ToSignatures(first), ToSignatures(second));
    }

    [Fact]
    public void Resolve_AssignsContiguousZeroBasedSortOrder()
    {
        var result = _resolver.Resolve(
        [
            Candidate("Docker", "docker", JobSkillRequirementType.Preferred, 60),
            Candidate("C#", "c#", JobSkillRequirementType.Required, 100),
            Candidate("PostgreSQL", "postgresql", JobSkillRequirementType.Mentioned, 30)
        ]);

        Assert.Equal([0, 1, 2], result.Select(item => item.SortOrder));
    }

    [Fact]
    public void Resolve_AmbiguousCandidateNeverReceivesSkillId()
    {
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate(
                "Azure",
                "azure",
                JobSkillRequirementType.Required,
                100,
                resolutionStatus: SkillResolutionStatus.Ambiguous)
        ]));

        Assert.Equal(SkillResolutionStatus.Ambiguous, result.ResolutionStatus);
        Assert.Null(result.ResolvedSkillId);
    }

    [Fact]
    public void Resolve_UnresolvedCandidateNeverReceivesSkillId()
    {
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate(
                "Unknown",
                "unknown",
                JobSkillRequirementType.Mentioned,
                30,
                resolutionStatus: SkillResolutionStatus.Unresolved)
        ]));

        Assert.Equal(SkillResolutionStatus.Unresolved, result.ResolutionStatus);
        Assert.Null(result.ResolvedSkillId);
    }

    [Fact]
    public void Resolve_UnresolvedDuplicateIsNotPromotedToExact()
    {
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate("Docker", "docker", JobSkillRequirementType.Required, 100),
            Candidate(
                "docker",
                "docker",
                JobSkillRequirementType.Preferred,
                60,
                resolutionStatus: SkillResolutionStatus.Unresolved)
        ]));

        Assert.Equal(SkillResolutionStatus.Unresolved, result.ResolutionStatus);
        Assert.Null(result.ResolvedSkillId);
    }

    [Fact]
    public void Resolve_MergedEvidenceIsCappedDeterministically()
    {
        var evidence = Enumerable.Range(0, 12)
            .Select(index => Evidence($"e{index}", index * 10))
            .ToImmutableArray();

        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate(
                "Docker",
                "docker",
                JobSkillRequirementType.Required,
                100,
                evidence: evidence)
        ]));

        Assert.Equal(ParsedSkillDuplicateResolverV1.MaximumEvidenceCount,
            result.Evidence.Length);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(index => index * 10),
            result.Evidence.Select(item => item.Start));
    }

    [Fact]
    public void Resolve_ExactAndAliasForSameSkill_PreservesExactResolution()
    {
        var skillId = Id(1);
        var result = Assert.Single(_resolver.Resolve(
        [
            Candidate("PostgreSQL", "postgresql", JobSkillRequirementType.Required, 100,
                skillId: skillId),
            Candidate("postgres", "postgresql", JobSkillRequirementType.Preferred, 60,
                skillId: skillId,
                resolutionStatus: SkillResolutionStatus.Alias)
        ]));

        Assert.Equal(SkillResolutionStatus.Exact, result.ResolutionStatus);
        Assert.Equal(skillId, result.ResolvedSkillId);
    }

    private ParsedSkillCandidate ResolveRequirementPair(
        JobSkillRequirementType firstType,
        int firstImportance,
        JobSkillRequirementType secondType,
        int secondImportance)
    {
        return Assert.Single(_resolver.Resolve(
        [
            Candidate("Docker", "docker", firstType, firstImportance),
            Candidate("docker", "docker", secondType, secondImportance)
        ]));
    }

    private static ParsedSkillCandidate Candidate(
        string name,
        string canonicalKey,
        JobSkillRequirementType requirementType,
        int importance,
        decimal? confidence = null,
        Guid? skillId = null,
        SkillCategory category = SkillCategory.Tool,
        SkillResolutionStatus resolutionStatus = SkillResolutionStatus.Exact,
        ImmutableArray<SkillEvidence> evidence = default)
    {
        if (evidence.IsDefault)
        {
            evidence = [Evidence(name, 0)];
        }

        var isResolved = resolutionStatus is
            SkillResolutionStatus.Exact or SkillResolutionStatus.Alias;
        Guid? resolvedId = isResolved ? skillId ?? Id(1) : null;

        return new ParsedSkillCandidate(
            99,
            name,
            canonicalKey,
            resolvedId,
            isResolved ? canonicalKey : null,
            isResolved ? category : null,
            requirementType,
            importance,
            confidence,
            evidence,
            resolutionStatus);
    }

    private static SkillEvidence Evidence(string text, int start)
    {
        return new SkillEvidence(text, start, text.Length);
    }

    private static string[] ToSignatures(
        ImmutableArray<ParsedSkillCandidate> candidates)
    {
        return candidates
            .Select(candidate => string.Join(
                '|',
                candidate.SortOrder,
                candidate.CandidateName,
                candidate.CanonicalKey,
                candidate.RequirementType,
                candidate.ProposedImportanceScore,
                candidate.ResolutionStatus,
                candidate.ResolvedSkillId,
                string.Join(',', candidate.Evidence.Select(item =>
                    $"{item.Start}:{item.Length}:{item.Text}"))))
            .ToArray();
    }

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
