using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Rules;

public static class RuleExecutor
{
    public static ImmutableArray<RuleResult> Execute(
        IEnumerable<IAnalysisRule> rules,
        RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(context);

        var ruleArray = rules.ToArray();

        if (ruleArray.Any(rule => rule is null))
        {
            throw new ArgumentException(
                "Rule collection cannot contain null values.",
                nameof(rules));
        }

        var orderedRules = ruleArray
            .OrderBy(rule => rule.RuleCode, StringComparer.Ordinal)
            .ToArray();

        var duplicateCode = orderedRules
            .GroupBy(rule => rule.RuleCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateCode is not null)
        {
            throw new InvalidOperationException(
                $"Rule code '{duplicateCode}' is registered more than once.");
        }

        var results = ImmutableArray.CreateBuilder<RuleResult>(orderedRules.Length);

        foreach (var rule in orderedRules)
        {
            var result = rule.Evaluate(context);
            results.Add(new RuleResult(
                rule.RuleCode,
                rule.Category,
                rule.Weight,
                result,
                rule.FailureIssue));
        }

        return results.MoveToImmutable();
    }
}
