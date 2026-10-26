using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record AnalysisIssueResult(
    AnalysisIssueType Type,
    IssueSeverity Severity,
    string Code,
    string Title,
    string Description,
    string? ResumeSection);
