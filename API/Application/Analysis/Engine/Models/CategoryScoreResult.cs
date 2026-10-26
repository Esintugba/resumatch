using Api.Application.Analysis.Engine.Rules;

namespace Api.Application.Analysis.Engine.Models;

public sealed record CategoryScoreResult
{
    public CategoryScoreResult(
        RuleCategory category,
        decimal weight,
        decimal score)
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

        if (score is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                "Category score must be between 0 and 100.");
        }

        Category = category;
        Weight = weight;
        Score = score;
    }

    public RuleCategory Category { get; }
    public decimal Weight { get; }
    public decimal Score { get; }
}
