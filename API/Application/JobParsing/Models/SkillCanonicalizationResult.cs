using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Models;

public sealed record SkillCanonicalizationResult
{
    public SkillCanonicalizationResult(
        SkillResolutionStatus status,
        string canonicalKey,
        ImmutableArray<SkillCatalogItem> matches)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "Skill resolution status is not defined.");
        }

        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            throw new ArgumentException(
                "Canonical skill key is required.",
                nameof(canonicalKey));
        }

        Matches = matches.IsDefault
            ? ImmutableArray<SkillCatalogItem>.Empty
            : matches;

        if (Matches.Any(match => match is null))
        {
            throw new ArgumentException(
                "Canonicalization matches cannot contain null values.",
                nameof(matches));
        }

        if (Matches.Select(match => match.Id).Distinct().Count() != Matches.Length)
        {
            throw new ArgumentException(
                "Canonicalization matches cannot contain duplicate skills.",
                nameof(matches));
        }

        ValidateMatchCount(status, Matches.Length);

        Status = status;
        CanonicalKey = canonicalKey;
    }

    public SkillResolutionStatus Status { get; }
    public string CanonicalKey { get; }
    public ImmutableArray<SkillCatalogItem> Matches { get; }

    public SkillCatalogItem? ResolvedSkill =>
        Status is SkillResolutionStatus.Exact or SkillResolutionStatus.Alias
            ? Matches[0]
            : null;

    private static void ValidateMatchCount(
        SkillResolutionStatus status,
        int matchCount)
    {
        if (status is SkillResolutionStatus.Exact or SkillResolutionStatus.Alias)
        {
            if (matchCount != 1)
            {
                throw new ArgumentException(
                    "Exact and alias resolutions require exactly one skill match.",
                    nameof(matchCount));
            }

            return;
        }

        if (status == SkillResolutionStatus.Ambiguous && matchCount < 2)
        {
            throw new ArgumentException(
                "An ambiguous resolution requires at least two skill matches.",
                nameof(matchCount));
        }

        if (status == SkillResolutionStatus.Unresolved && matchCount != 0)
        {
            throw new ArgumentException(
                "An unresolved candidate cannot contain skill matches.",
                nameof(matchCount));
        }
    }
}
