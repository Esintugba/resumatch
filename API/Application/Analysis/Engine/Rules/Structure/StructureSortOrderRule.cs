using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Structure;

public sealed class StructureSortOrderRule : IAnalysisRule
{
    public const string Code = "ATS.STR.004";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Structure;
    public decimal Weight => 20m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Structure,
        IssueSeverity.Warning,
        "Resume");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var orderingScopes = BuildOrderingScopes(context);
        if (orderingScopes.Count == 0)
        {
            return null;
        }

        return orderingScopes.All(IsValidOrderingScope) ? 1m : 0m;
    }

    private static List<IReadOnlyCollection<int>> BuildOrderingScopes(
        RuleEvaluationContext context)
    {
        var resume = context.Resume;
        var scopes = new List<IReadOnlyCollection<int>>();

        AddScope(scopes, resume.Experiences, item => item.SortOrder);
        AddScope(scopes, resume.Educations, item => item.SortOrder);
        AddScope(scopes, resume.Skills, item => item.SortOrder);
        AddScope(scopes, resume.Projects, item => item.SortOrder);
        AddScope(scopes, resume.Certifications, item => item.SortOrder);
        AddScope(scopes, resume.Languages, item => item.SortOrder);

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                AddScope(scopes, experience.Bullets, bullet => bullet.SortOrder);
            }
        }

        return scopes;
    }

    private static void AddScope<TItem>(
        ICollection<IReadOnlyCollection<int>> scopes,
        ImmutableArray<TItem> items,
        Func<TItem, int> getSortOrder)
    {
        if (items.IsDefaultOrEmpty)
        {
            return;
        }

        var sortOrders = items.Select(getSortOrder).ToArray();
        if (sortOrders.Length > 0)
        {
            scopes.Add(sortOrders);
        }
    }

    private static bool IsValidOrderingScope(
        IReadOnlyCollection<int> sortOrders)
    {
        return sortOrders.All(sortOrder => sortOrder >= 0) &&
               sortOrders.Distinct().Count() == sortOrders.Count;
    }
}
