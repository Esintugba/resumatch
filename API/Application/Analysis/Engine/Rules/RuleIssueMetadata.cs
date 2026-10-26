using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules;

public sealed record RuleIssueMetadata
{
    public RuleIssueMetadata(
        AnalysisIssueType type,
        IssueSeverity severity,
        string? resumeSection)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                "Analysis issue type is not defined.");
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                "Issue severity is not defined.");
        }

        Type = type;
        Severity = severity;
        ResumeSection = string.IsNullOrWhiteSpace(resumeSection)
            ? null
            : resumeSection;
    }

    public AnalysisIssueType Type { get; }
    public IssueSeverity Severity { get; }
    public string? ResumeSection { get; }
}
