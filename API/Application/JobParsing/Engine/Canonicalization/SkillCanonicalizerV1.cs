using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Engine.Canonicalization;

public sealed class SkillCanonicalizerV1 : ISkillCanonicalizer
{
    public SkillCanonicalizationResult Resolve(
        ExtractedSkillCandidate candidate,
        IReadOnlyCollection<SkillCatalogItem> skillCatalog)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(skillCatalog);

        if (skillCatalog.Any(skill => skill is null))
        {
            throw new ArgumentException(
                "Skill catalog cannot contain null values.",
                nameof(skillCatalog));
        }

        var normalizedCandidate = Normalize(candidate.CandidateName);
        var catalog = skillCatalog
            .DistinctBy(skill => skill.Id)
            .Select(skill => new CatalogEntry(
                skill,
                Normalize(skill.NormalizedName)))
            .ToArray();

        var exactMatches = FindMatches(catalog, normalizedCandidate);
        if (exactMatches.Length > 0)
        {
            return CreateResult(
                normalizedCandidate,
                exactMatches,
                SkillResolutionStatus.Exact);
        }

        if (SkillAliasCatalogV1.TryResolve(
                normalizedCandidate,
                out var aliasTarget))
        {
            var aliasMatches = FindMatches(catalog, aliasTarget);
            if (aliasMatches.Length > 0)
            {
                return CreateResult(
                    aliasTarget,
                    aliasMatches,
                    SkillResolutionStatus.Alias);
            }
        }

        return new SkillCanonicalizationResult(
            SkillResolutionStatus.Unresolved,
            normalizedCandidate,
            []);
    }

    private static SkillCanonicalizationResult CreateResult(
        string canonicalKey,
        SkillCatalogItem[] matches,
        SkillResolutionStatus singleMatchStatus)
    {
        return new SkillCanonicalizationResult(
            matches.Length == 1
                ? singleMatchStatus
                : SkillResolutionStatus.Ambiguous,
            canonicalKey,
            matches.ToImmutableArray());
    }

    private static SkillCatalogItem[] FindMatches(
        IEnumerable<CatalogEntry> catalog,
        string normalizedName)
    {
        return catalog
            .Where(entry => string.Equals(
                entry.NormalizedName,
                normalizedName,
                StringComparison.Ordinal))
            .Select(entry => entry.Skill)
            .OrderBy(skill => skill.Category)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .ThenBy(skill => skill.Id)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        var normalized = JobDescriptionPreprocessor.NormalizeForMatching(value);
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Canonical skill name cannot be empty.",
                nameof(value));
        }

        return normalized;
    }

    private sealed record CatalogEntry(
        SkillCatalogItem Skill,
        string NormalizedName);
}
