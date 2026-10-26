using System.Collections.Immutable;

namespace Api.Application.Analysis.Engine.Models;

public sealed record AnalysisResult
{
    public AnalysisResult(
        decimal atsScore,
        decimal? jobMatchScore,
        decimal structureScore,
        decimal formattingScore,
        decimal contentScore,
        decimal keywordScore,
        string analyzerVersion,
        ImmutableArray<CategoryScoreResult> categoryScores,
        ImmutableArray<RuleResult> ruleResults,
        ImmutableArray<AnalysisIssueResult> issues,
        ImmutableArray<KeywordMatchResult> keywordMatches,
        ImmutableArray<AnalysisRecommendationResult> recommendations)
    {
        ValidateScore(atsScore, nameof(atsScore));
        ValidateOptionalScore(jobMatchScore, nameof(jobMatchScore));
        ValidateScore(structureScore, nameof(structureScore));
        ValidateScore(formattingScore, nameof(formattingScore));
        ValidateScore(contentScore, nameof(contentScore));
        ValidateScore(keywordScore, nameof(keywordScore));

        if (string.IsNullOrWhiteSpace(analyzerVersion))
        {
            throw new ArgumentException(
                "Analyzer version is required.",
                nameof(analyzerVersion));
        }

        AtsScore = atsScore;
        JobMatchScore = jobMatchScore;
        StructureScore = structureScore;
        FormattingScore = formattingScore;
        ContentScore = contentScore;
        KeywordScore = keywordScore;
        AnalyzerVersion = analyzerVersion;
        CategoryScores = categoryScores.IsDefault
            ? ImmutableArray<CategoryScoreResult>.Empty
            : categoryScores;
        RuleResults = ruleResults.IsDefault
            ? ImmutableArray<RuleResult>.Empty
            : ruleResults;
        Issues = issues.IsDefault
            ? ImmutableArray<AnalysisIssueResult>.Empty
            : issues;
        KeywordMatches = keywordMatches.IsDefault
            ? ImmutableArray<KeywordMatchResult>.Empty
            : keywordMatches;
        Recommendations = recommendations.IsDefault
            ? ImmutableArray<AnalysisRecommendationResult>.Empty
            : recommendations;
    }

    public decimal AtsScore { get; }
    public decimal? JobMatchScore { get; }
    public decimal StructureScore { get; }
    public decimal FormattingScore { get; }
    public decimal ContentScore { get; }
    public decimal KeywordScore { get; }
    public string AnalyzerVersion { get; }
    public ImmutableArray<CategoryScoreResult> CategoryScores { get; }
    public ImmutableArray<RuleResult> RuleResults { get; }
    public ImmutableArray<AnalysisIssueResult> Issues { get; }
    public ImmutableArray<KeywordMatchResult> KeywordMatches { get; }
    public ImmutableArray<AnalysisRecommendationResult> Recommendations { get; }

    private static void ValidateScore(decimal score, string parameterName)
    {
        if (score is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Score must be between 0 and 100.");
        }
    }

    private static void ValidateOptionalScore(
        decimal? score,
        string parameterName)
    {
        if (score.HasValue)
        {
            ValidateScore(score.Value, parameterName);
        }
    }
}
