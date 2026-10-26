using System.Collections.Immutable;

namespace Api.Application.Analysis.Engine.Rules;

public sealed record CategoryDefinition
{
    public CategoryDefinition(RuleCategory category, decimal weight)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                "Rule category is not defined.");
        }

        if (weight is <= 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                "Category weight must be greater than 0 and at most 100.");
        }

        Category = category;
        Weight = weight;
    }

    public RuleCategory Category { get; }
    public decimal Weight { get; }
}

public static class AtsCategoryDefinitions
{
    public const decimal StructureWeight = 20m;
    public const decimal FormattingWeight = 10m;
    public const decimal ContactWeight = 15m;
    public const decimal ContentWeight = 20m;
    public const decimal SkillsWeight = 15m;
    public const decimal ExperienceWeight = 20m;

    public static ImmutableArray<CategoryDefinition> All { get; } =
    [
        new(RuleCategory.Structure, StructureWeight),
        new(RuleCategory.Formatting, FormattingWeight),
        new(RuleCategory.Contact, ContactWeight),
        new(RuleCategory.Content, ContentWeight),
        new(RuleCategory.Skills, SkillsWeight),
        new(RuleCategory.Experience, ExperienceWeight)
    ];
}
