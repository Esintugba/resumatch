using Api.Entities.Enums;

namespace Api.Entities;

public class AnalysisRecommendation : BaseEntity
{
    public Guid ResumeAnalysisId { get; set; }

    public string? RuleCode { get; set; }
    public int? SortOrder { get; set; }

    public RecommendationType Type { get; set; }

    public RecommendationPriority Priority { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string? TargetSection { get; set; }

    public string? OriginalContent { get; set; }
    public string? SuggestedContent { get; set; }

    public bool IsAiGenerated { get; set; }

    public RecommendationStatus Status { get; set; }

    public ResumeAnalysis ResumeAnalysis { get; set; } = null!;
}
