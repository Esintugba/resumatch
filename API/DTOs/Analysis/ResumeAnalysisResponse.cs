using Api.Entities.Enums;

namespace Api.DTOs.Analysis;

public sealed class ResumeAnalysisResponse
{
    public Guid Id { get; init; }
    public Guid ResumeId { get; init; }
    public Guid? JobPostingId { get; init; }
    public uint? SourceJobPostingVersion { get; init; }
    public string? SourceJobPostingInputHash { get; init; }
    public string? SourceJobPostingSkillSetHash { get; init; }
    public AnalysisType Type { get; init; }
    public decimal AtsScore { get; init; }
    public decimal? JobMatchScore { get; init; }
    public decimal StructureScore { get; init; }
    public decimal FormattingScore { get; init; }
    public decimal ContentScore { get; init; }
    public decimal KeywordScore { get; init; }
    public string? Summary { get; init; }
    public DateTimeOffset AnalyzedAt { get; init; }
    public string AnalyzerVersion { get; init; } = string.Empty;
    public List<AnalysisIssueResponse> Issues { get; init; } = new();
    public List<KeywordMatchResponse> KeywordMatches { get; init; } = new();
    public List<AnalysisRecommendationResponse> Recommendations { get; init; } = new();
}
