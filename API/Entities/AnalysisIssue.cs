using Api.Entities.Enums;

namespace Api.Entities;

public class AnalysisIssue : BaseEntity
{
    public Guid ResumeAnalysisId { get; set; }

    public AnalysisIssueType Type { get; set; }
    public IssueSeverity Severity { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string? ResumeSection { get; set; }

    public ResumeAnalysis ResumeAnalysis { get; set; } = null!;
}
