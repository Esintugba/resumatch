using Api.Application.Analysis.Engine.Rules;

namespace Api.Application.Analysis.Engine.Models;

public sealed record RuleResult
{
    public RuleResult(
        string ruleCode,
        RuleCategory category,
        decimal ruleWeight,
        decimal? result,
        RuleIssueMetadata failureIssue)
    {
        if (string.IsNullOrWhiteSpace(ruleCode))
        {
            throw new ArgumentException("Rule code is required.", nameof(ruleCode));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                "Rule category is not defined.");
        }

        if (ruleWeight is <= 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ruleWeight),
                "Rule weight must be greater than 0 and at most 100.");
        }

        if (result is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "Rule result must be between 0 and 1 when applicable.");
        }

        ArgumentNullException.ThrowIfNull(failureIssue);

        RuleCode = ruleCode;
        Category = category;
        RuleWeight = ruleWeight;
        Result = result;
        FailureIssue = failureIssue;
    }

    public string RuleCode { get; }
    public RuleCategory Category { get; }
    public decimal RuleWeight { get; }
    public decimal? Result { get; }
    public RuleIssueMetadata FailureIssue { get; }
    public bool IsApplicable => Result.HasValue;
    public bool ShouldCreateIssue => Result is >= 0m and < 1m;
}
