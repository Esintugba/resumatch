using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Keywords;

internal sealed record CanonicalSkillDescriptor(
    string Key,
    string? SearchPhrase,
    string DisplayName)
{
    public static CanonicalSkillDescriptor Create(SkillSnapshot skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var normalizedName = TextNormalizer.Normalize(skill.NormalizedName);
        if (normalizedName.Length > 0)
        {
            return new CanonicalSkillDescriptor(
                $"name:{normalizedName}",
                normalizedName,
                GetDisplayName(skill, normalizedName));
        }

        var fallbackName = TextNormalizer.Normalize(skill.Name);
        if (fallbackName.Length > 0)
        {
            return new CanonicalSkillDescriptor(
                $"name:{fallbackName}",
                fallbackName,
                GetDisplayName(skill, fallbackName));
        }

        if (skill.Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A skill without a canonical name must have a non-empty identifier.");
        }

        var identifier = skill.Id.ToString("D");
        return new CanonicalSkillDescriptor(
            $"id:{identifier}",
            null,
            identifier);
    }

    private static string GetDisplayName(
        SkillSnapshot skill,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(skill.Name)
            ? fallback
            : skill.Name.Trim();
    }
}

internal sealed record CanonicalJobSkillRequirement(
    JobPostingSkillSnapshot Skill,
    CanonicalSkillDescriptor CanonicalSkill);

internal static class JobSkillRequirementSelector
{
    public static ImmutableArray<CanonicalJobSkillRequirement> Select(
        JobPostingSnapshot jobPosting)
    {
        ArgumentNullException.ThrowIfNull(jobPosting);

        if (jobPosting.Skills.IsDefaultOrEmpty)
        {
            return ImmutableArray<CanonicalJobSkillRequirement>.Empty;
        }

        foreach (var jobPostingSkill in jobPosting.Skills)
        {
            ValidateImportanceScore(jobPostingSkill.ImportanceScore);
            _ = GetRequirementPriority(jobPostingSkill.RequirementType);
        }

        var selected = jobPosting.Skills
            .Select(skill => new CanonicalJobSkillRequirement(
                skill,
                CanonicalSkillDescriptor.Create(skill.Skill)))
            .GroupBy(
                requirement => requirement.CanonicalSkill.Key,
                StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(requirement => GetRequirementPriority(
                    requirement.Skill.RequirementType))
                .ThenByDescending(requirement =>
                    requirement.Skill.ImportanceScore)
                .ThenBy(requirement => requirement.Skill.Skill.Id)
                .ThenBy(requirement => requirement.Skill.Id)
                .First())
            .OrderBy(requirement => GetRequirementPriority(
                requirement.Skill.RequirementType))
            .ThenByDescending(requirement => requirement.Skill.ImportanceScore)
            .ThenBy(requirement => requirement.Skill.Skill.Id)
            .ThenBy(requirement => requirement.CanonicalSkill.Key,
                StringComparer.Ordinal)
            .ToImmutableArray();

        var duplicateSkillId = selected
            .GroupBy(requirement => requirement.Skill.Skill.Id)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateSkillId.HasValue)
        {
            throw new InvalidOperationException(
                $"Selected canonical requirements contain duplicate SkillId " +
                $"'{duplicateSkillId.Value}'.");
        }

        return selected;
    }

    public static int GetRequirementPriority(
        JobSkillRequirementType requirementType)
    {
        return requirementType switch
        {
            JobSkillRequirementType.Required => 0,
            JobSkillRequirementType.Preferred => 1,
            JobSkillRequirementType.Mentioned => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Job skill requirement type is not defined.")
        };
    }

    private static void ValidateImportanceScore(int importanceScore)
    {
        if (importanceScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(importanceScore),
                "Job skill importance score must be between 0 and 100.");
        }
    }
}
