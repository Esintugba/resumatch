using Api.Entities.Enums;

namespace Api.DTOs.Analysis;

public sealed class AnalysisIssueResponse
{
    public Guid Id { get; init; }
    public AnalysisIssueType Type { get; init; }
    public IssueSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ResumeSection { get; init; }
}
