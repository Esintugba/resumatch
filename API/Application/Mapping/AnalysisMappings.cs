using Api.DTOs.Analysis;
using Api.Entities;

namespace Api.Application.Mapping;

public static class AnalysisMappings
{
    public static ResumeAnalysisResponse ToResponse(this ResumeAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return new ResumeAnalysisResponse
        {
            Id = analysis.Id,
            ResumeId = analysis.ResumeId,
            JobPostingId = analysis.JobPostingId,
            SourceJobPostingVersion = analysis.SourceJobPostingVersion,
            SourceJobPostingInputHash = analysis.SourceJobPostingInputHash,
            SourceJobPostingSkillSetHash =
                analysis.SourceJobPostingSkillSetHash,
            Type = analysis.Type,
            AtsScore = analysis.AtsScore,
            JobMatchScore = analysis.JobMatchScore,
            StructureScore = analysis.StructureScore,
            FormattingScore = analysis.FormattingScore,
            ContentScore = analysis.ContentScore,
            KeywordScore = analysis.KeywordScore,
            Summary = analysis.Summary,
            AnalyzedAt = analysis.AnalyzedAt,
            AnalyzerVersion = analysis.AnalyzerVersion,
            Issues = analysis.Issues
                .OrderBy(issue => issue.Id)
                .Select(issue => issue.ToResponse())
                .ToList(),
            KeywordMatches = analysis.KeywordMatches
                .OrderBy(keywordMatch => keywordMatch.Id)
                .Select(keywordMatch => keywordMatch.ToResponse())
                .ToList(),
            Recommendations = analysis.Recommendations
                .OrderBy(recommendation => recommendation.SortOrder.HasValue ? 0 : 1)
                .ThenBy(recommendation => recommendation.SortOrder)
                .ThenByDescending(recommendation => recommendation.Priority)
                .ThenBy(recommendation => recommendation.Type)
                .ThenBy(
                    recommendation => recommendation.TargetSection,
                    StringComparer.Ordinal)
                .ThenBy(
                    recommendation => recommendation.Title,
                    StringComparer.Ordinal)
                .ThenBy(
                    recommendation => recommendation.Description,
                    StringComparer.Ordinal)
                // Legacy rows have no persisted engine order. Id is only a
                // stable final tie-breaker and does not represent engine priority.
                .ThenBy(recommendation => recommendation.Id)
                .Select(recommendation => recommendation.ToResponse())
                .ToList()
        };
    }

    public static AnalysisIssueResponse ToResponse(this AnalysisIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new AnalysisIssueResponse
        {
            Id = issue.Id,
            Type = issue.Type,
            Severity = issue.Severity,
            Code = issue.Code,
            Title = issue.Title,
            Description = issue.Description,
            ResumeSection = issue.ResumeSection
        };
    }

    public static KeywordMatchResponse ToResponse(this KeywordMatch keywordMatch)
    {
        ArgumentNullException.ThrowIfNull(keywordMatch);

        return new KeywordMatchResponse
        {
            Id = keywordMatch.Id,
            SkillId = keywordMatch.SkillId,
            Keyword = keywordMatch.Keyword,
            Status = keywordMatch.Status,
            Weight = keywordMatch.Weight,
            ResumeSection = keywordMatch.ResumeSection
        };
    }

    public static AnalysisRecommendationResponse ToResponse(
        this AnalysisRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        return new AnalysisRecommendationResponse
        {
            Id = recommendation.Id,
            RuleCode = recommendation.RuleCode,
            SortOrder = recommendation.SortOrder,
            Type = recommendation.Type,
            Priority = recommendation.Priority,
            Title = recommendation.Title,
            Description = recommendation.Description,
            TargetSection = recommendation.TargetSection,
            OriginalContent = recommendation.OriginalContent,
            SuggestedContent = recommendation.SuggestedContent,
            IsAiGenerated = recommendation.IsAiGenerated,
            Status = recommendation.Status
        };
    }
}
