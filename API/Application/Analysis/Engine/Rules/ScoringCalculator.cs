using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Rules;

public static class ScoringCalculator
{
    private const int ScorePrecision = 2;

    public static decimal CalculateCategoryScore(
        CategoryDefinition category,
        IEnumerable<RuleResult> ruleResults)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(ruleResults);

        var applicableRules = ruleResults
            .Where(result =>
                result.Category == category.Category &&
                result.IsApplicable)
            .ToArray();

        var applicableWeight = applicableRules.Sum(result => result.RuleWeight);
        if (applicableWeight <= 0m)
        {
            throw new InvalidOperationException(
                $"Category '{category.Category}' has no applicable weighted rules.");
        }

        var weightedResult = applicableRules.Sum(
            result => result.RuleWeight * result.Result!.Value);

        return EnsureScoreRange(
            100m * weightedResult / applicableWeight,
            category.Category.ToString());
    }

    public static ImmutableArray<CategoryScoreResult> CalculateCategoryScores(
        IEnumerable<CategoryDefinition> categories,
        IEnumerable<RuleResult> ruleResults)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(ruleResults);

        var categoryArray = categories.ToArray();
        var resultArray = ruleResults.ToArray();

        var duplicateCategory = categoryArray
            .GroupBy(category => category.Category)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateCategory.HasValue)
        {
            throw new InvalidOperationException(
                $"Category '{duplicateCategory.Value}' is defined more than once.");
        }

        return categoryArray
            .OrderBy(category => category.Category)
            .Select(category => new CategoryScoreResult(
                category.Category,
                category.Weight,
                CalculateCategoryScore(category, resultArray)))
            .ToImmutableArray();
    }

    public static AtsScoreCalculationResult CalculateAtsScore(
        IEnumerable<RuleResult> ruleResults)
    {
        ArgumentNullException.ThrowIfNull(ruleResults);

        var categoryScores = CalculateCategoryScores(
            AtsCategoryDefinitions.All,
            ruleResults);

        return CalculateAtsScore(categoryScores);
    }

    public static AtsScoreCalculationResult CalculateAtsScore(
        IEnumerable<CategoryScoreResult> categoryScores)
    {
        ArgumentNullException.ThrowIfNull(categoryScores);

        var scores = categoryScores.ToArray();
        ValidateAtsCategoryDefinitions(scores);

        var structure = GetRequiredScore(scores, RuleCategory.Structure);
        var formatting = GetRequiredScore(scores, RuleCategory.Formatting);
        var contact = GetRequiredScore(scores, RuleCategory.Contact);
        var content = GetRequiredScore(scores, RuleCategory.Content);
        var skills = GetRequiredScore(scores, RuleCategory.Skills);
        var experience = GetRequiredScore(scores, RuleCategory.Experience);

        var rawAtsScore =
            structure * 0.20m +
            formatting * 0.10m +
            contact * 0.15m +
            content * 0.20m +
            skills * 0.15m +
            experience * 0.20m;

        var rawStructureScore =
            (structure * AtsCategoryDefinitions.StructureWeight +
             contact * AtsCategoryDefinitions.ContactWeight) /
            (AtsCategoryDefinitions.StructureWeight +
             AtsCategoryDefinitions.ContactWeight);

        var rawFormattingScore = formatting;

        var rawContentScore =
            (content * AtsCategoryDefinitions.ContentWeight +
             experience * AtsCategoryDefinitions.ExperienceWeight) /
            (AtsCategoryDefinitions.ContentWeight +
             AtsCategoryDefinitions.ExperienceWeight);

        var rawKeywordScore = skills;

        var rawAtsScoreFromPersistedFormula =
            rawStructureScore * 0.35m +
            rawFormattingScore * 0.10m +
            rawContentScore * 0.40m +
            rawKeywordScore * 0.15m;

        var atsScore = RoundScore(rawAtsScore);
        var equivalentAtsScore = RoundScore(rawAtsScoreFromPersistedFormula);
        if (atsScore != equivalentAtsScore)
        {
            throw new InvalidOperationException(
                "ATS category and persisted-score formulas produced " +
                "different final results.");
        }

        var structureScore = RoundScore(rawStructureScore);
        var formattingScore = RoundScore(rawFormattingScore);
        var contentScore = RoundScore(rawContentScore);
        var keywordScore = RoundScore(rawKeywordScore);

        var finalizedCategoryScores = AtsCategoryDefinitions.All
            .Select(definition => new CategoryScoreResult(
                definition.Category,
                definition.Weight,
                RoundScore(GetRequiredScore(scores, definition.Category))))
            .ToImmutableArray();

        return new AtsScoreCalculationResult(
            atsScore,
            structureScore,
            formattingScore,
            contentScore,
            keywordScore,
            finalizedCategoryScores);
    }

    private static void ValidateAtsCategoryDefinitions(
        IReadOnlyCollection<CategoryScoreResult> scores)
    {
        foreach (var definition in AtsCategoryDefinitions.All)
        {
            var matchingScores = scores
                .Where(score => score.Category == definition.Category)
                .ToArray();

            if (matchingScores.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Exactly one score is required for category " +
                    $"'{definition.Category}'.");
            }

            if (matchingScores[0].Weight != definition.Weight)
            {
                throw new InvalidOperationException(
                    $"Category '{definition.Category}' must use weight " +
                    $"{definition.Weight}.");
            }
        }
    }

    private static decimal GetRequiredScore(
        IEnumerable<CategoryScoreResult> scores,
        RuleCategory category)
    {
        return scores.Single(score => score.Category == category).Score;
    }

    private static decimal RoundScore(decimal score)
    {
        return EnsureScoreRange(
            decimal.Round(
                score,
                ScorePrecision,
                MidpointRounding.AwayFromZero),
            nameof(score));
    }

    private static decimal EnsureScoreRange(decimal score, string scoreName)
    {
        if (score is < 0m or > 100m)
        {
            throw new InvalidOperationException(
                $"Score '{scoreName}' must be between 0 and 100.");
        }

        return score;
    }
}
