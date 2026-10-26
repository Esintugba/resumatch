using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Keywords;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.JobMatching;

internal static class JobMatchWeightAllocator
{
    private const decimal RequiredGroupWeight = 70m;
    private const decimal PreferredGroupWeight = 20m;
    private const decimal MentionedGroupWeight = 10m;

    public static ImmutableDictionary<string, decimal> Allocate(
        ImmutableArray<CanonicalJobSkillRequirement> requirements)
    {
        if (requirements.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, decimal>.Empty;
        }

        var activeRequirementTypes = requirements
            .Select(requirement => requirement.Skill.RequirementType)
            .Distinct()
            .ToArray();
        var activeBaseWeight = activeRequirementTypes.Sum(GetBaseGroupWeight);

        var allocations = new List<WeightAllocation>(requirements.Length);
        foreach (var group in requirements.GroupBy(
                     requirement => requirement.Skill.RequirementType))
        {
            var groupWeight =
                GetBaseGroupWeight(group.Key) * 100m / activeBaseWeight;
            var groupRequirements = group.ToArray();
            var importanceTotal = groupRequirements.Sum(requirement =>
                Math.Max(1, requirement.Skill.ImportanceScore));

            foreach (var requirement in groupRequirements)
            {
                var effectiveImportance = Math.Max(
                    1,
                    requirement.Skill.ImportanceScore);
                var rawWeight =
                    groupWeight * effectiveImportance / importanceTotal;

                allocations.Add(new WeightAllocation(
                    requirement.CanonicalSkill.Key,
                    RoundWeight(rawWeight)));
            }
        }

        var difference = 100m - allocations.Sum(item => item.FinalWeight);
        if (difference != 0m)
        {
            var adjustmentTarget = allocations
                .OrderByDescending(item => item.FinalWeight)
                .ThenBy(item => item.CanonicalKey, StringComparer.Ordinal)
                .First();
            var targetIndex = allocations.IndexOf(adjustmentTarget);
            var adjustedWeight = RoundWeight(
                adjustmentTarget.FinalWeight + difference);

            if (adjustedWeight is < 0m or > 100m)
            {
                throw new InvalidOperationException(
                    "Rounding adjustment produced an invalid keyword weight.");
            }

            allocations[targetIndex] = adjustmentTarget with
            {
                FinalWeight = adjustedWeight
            };
        }

        if (allocations.Sum(item => item.FinalWeight) != 100m)
        {
            throw new InvalidOperationException(
                "Final keyword weights must total exactly 100.00.");
        }

        return allocations.ToImmutableDictionary(
            item => item.CanonicalKey,
            item => item.FinalWeight,
            StringComparer.Ordinal);
    }

    private static decimal GetBaseGroupWeight(
        JobSkillRequirementType requirementType)
    {
        return requirementType switch
        {
            JobSkillRequirementType.Required => RequiredGroupWeight,
            JobSkillRequirementType.Preferred => PreferredGroupWeight,
            JobSkillRequirementType.Mentioned => MentionedGroupWeight,
            _ => throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Job skill requirement type is not defined.")
        };
    }

    private static decimal RoundWeight(decimal weight)
    {
        return decimal.Round(weight, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record WeightAllocation(
        string CanonicalKey,
        decimal FinalWeight);
}
