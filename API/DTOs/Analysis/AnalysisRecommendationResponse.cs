using Api.Entities.Enums;

namespace Api.DTOs.Analysis;

public sealed class AnalysisRecommendationResponse
{
    public Guid Id { get; init; }
    public string? RuleCode { get; init; }
    public int? SortOrder { get; init; }
    public RecommendationType Type { get; init; }
    public RecommendationPriority Priority { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? TargetSection { get; init; }
    public string? OriginalContent { get; init; }
    public string? SuggestedContent { get; init; }
    public bool IsAiGenerated { get; init; }
    public RecommendationStatus Status { get; init; }
}
