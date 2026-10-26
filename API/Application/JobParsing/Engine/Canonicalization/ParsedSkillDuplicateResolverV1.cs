using System.Collections.Immutable;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Engine.Canonicalization;

public sealed class ParsedSkillDuplicateResolverV1
{
    public const int MaximumEvidenceCount = 10;

    public ImmutableArray<ParsedSkillCandidate> Resolve(
        ImmutableArray<ParsedSkillCandidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return ImmutableArray<ParsedSkillCandidate>.Empty;
        }

        if (candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException(
                "Parsed skill candidates cannot contain null values.",
                nameof(candidates));
        }

        var merged = candidates
            .GroupBy(candidate => candidate.CanonicalKey, StringComparer.Ordinal)
            .Select(MergeGroup)
            .OrderBy(candidate => GetRequirementPriority(
                candidate.RequirementType))
            .ThenByDescending(candidate => candidate.ProposedImportanceScore)
            .ThenBy(candidate => candidate.CanonicalKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CandidateName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ResolutionStatus)
            .ThenBy(candidate => candidate.ResolvedSkillId)
            .ToArray();

        return merged
            .Select((candidate, index) => new ParsedSkillCandidate(
                index,
                candidate.CandidateName,
                candidate.CanonicalKey,
                candidate.ResolvedSkillId,
                candidate.ResolvedSkillName,
                candidate.ResolvedCategory,
                candidate.RequirementType,
                candidate.ProposedImportanceScore,
                candidate.Confidence,
                candidate.Evidence,
                candidate.ResolutionStatus))
            .ToImmutableArray();
    }

    private static MergedCandidate MergeGroup(
        IGrouping<string, ParsedSkillCandidate> group)
    {
        var items = group.ToArray();
        var winner = items
            .OrderBy(candidate => GetRequirementPriority(
                candidate.RequirementType))
            .ThenByDescending(candidate => candidate.ProposedImportanceScore)
            .ThenBy(candidate => candidate.CandidateName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ResolutionStatus)
            .ThenBy(candidate => candidate.ResolvedSkillId)
            .First();
        var resolution = ResolveGroupResolution(items);
        var evidence = items
            .SelectMany(candidate => candidate.Evidence)
            .DistinctBy(item => new EvidenceKey(
                item.Text,
                item.Start,
                item.Length))
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Length)
            .ThenBy(item => item.Text, StringComparer.Ordinal)
            .Take(MaximumEvidenceCount)
            .ToImmutableArray();
        var confidenceValues = items
            .Where(candidate => candidate.Confidence.HasValue)
            .Select(candidate => candidate.Confidence!.Value)
            .ToArray();

        return new MergedCandidate(
            winner.CandidateName,
            group.Key,
            resolution.SkillId,
            resolution.SkillName,
            resolution.Category,
            winner.RequirementType,
            winner.ProposedImportanceScore,
            confidenceValues.Length == 0 ? null : confidenceValues.Max(),
            evidence,
            resolution.Status);
    }

    private static GroupResolution ResolveGroupResolution(
        IReadOnlyCollection<ParsedSkillCandidate> items)
    {
        if (items.Any(candidate =>
                candidate.ResolutionStatus == SkillResolutionStatus.Ambiguous))
        {
            return GroupResolution.Ambiguous;
        }

        if (items.Any(candidate =>
                candidate.ResolutionStatus == SkillResolutionStatus.Unresolved))
        {
            return GroupResolution.Unresolved;
        }

        var resolvedIdentities = items
            .Select(candidate => new ResolvedIdentity(
                candidate.ResolvedSkillId!.Value,
                candidate.ResolvedSkillName!,
                candidate.ResolvedCategory!.Value))
            .Distinct()
            .ToArray();

        if (resolvedIdentities.Length != 1)
        {
            return GroupResolution.Ambiguous;
        }

        var identity = resolvedIdentities[0];
        var status = items.Any(candidate =>
            candidate.ResolutionStatus == SkillResolutionStatus.Exact)
                ? SkillResolutionStatus.Exact
                : SkillResolutionStatus.Alias;

        return new GroupResolution(
            status,
            identity.SkillId,
            identity.SkillName,
            identity.Category);
    }

    private static int GetRequirementPriority(
        JobSkillRequirementType requirementType)
    {
        return requirementType switch
        {
            JobSkillRequirementType.Required => 0,
            JobSkillRequirementType.Preferred => 1,
            JobSkillRequirementType.Mentioned => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Skill requirement type is not defined.")
        };
    }

    private sealed record MergedCandidate(
        string CandidateName,
        string CanonicalKey,
        Guid? ResolvedSkillId,
        string? ResolvedSkillName,
        SkillCategory? ResolvedCategory,
        JobSkillRequirementType RequirementType,
        int ProposedImportanceScore,
        decimal? Confidence,
        ImmutableArray<SkillEvidence> Evidence,
        SkillResolutionStatus ResolutionStatus);

    private sealed record GroupResolution(
        SkillResolutionStatus Status,
        Guid? SkillId,
        string? SkillName,
        SkillCategory? Category)
    {
        public static GroupResolution Ambiguous { get; } = new(
            SkillResolutionStatus.Ambiguous,
            null,
            null,
            null);

        public static GroupResolution Unresolved { get; } = new(
            SkillResolutionStatus.Unresolved,
            null,
            null,
            null);
    }

    private sealed record ResolvedIdentity(
        Guid SkillId,
        string SkillName,
        SkillCategory Category);

    private sealed record EvidenceKey(
        string Text,
        int Start,
        int Length);
}
