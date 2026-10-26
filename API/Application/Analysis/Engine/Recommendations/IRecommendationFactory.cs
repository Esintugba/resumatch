using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Recommendations;

public interface IRecommendationFactory
{
    ImmutableArray<AnalysisRecommendationResult> Create(
        IEnumerable<RuleResult> ruleResults,
        IEnumerable<AnalysisIssueResult> issues,
        IEnumerable<KeywordMatchResult> keywordMatches);
}
