using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record AnalysisRecommendationResult(
    string RuleCode,
    RecommendationType Type,
    RecommendationPriority Priority,
    string Title,
    string Description,
    string? TargetSection,
    string? OriginalContent,
    string? SuggestedContent,
    bool IsAiGenerated,
    RecommendationStatus Status);
