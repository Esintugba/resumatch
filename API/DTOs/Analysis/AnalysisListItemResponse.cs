using Api.Entities.Enums;

namespace Api.DTOs.Analysis;

public sealed class AnalysisListItemResponse
{
    public Guid Id { get; init; }
    public Guid ResumeId { get; init; }
    public Guid? JobPostingId { get; init; }
    public AnalysisType Type { get; init; }
    public decimal AtsScore { get; init; }
    public decimal? JobMatchScore { get; init; }
    public DateTimeOffset AnalyzedAt { get; init; }
    public string AnalyzerVersion { get; init; } = string.Empty;
}
