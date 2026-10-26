using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record JobPostingSnapshot(
    Guid Id,
    string Title,
    string? CompanyName,
    string? Location,
    WorkType? WorkType,
    EmploymentType? EmploymentType,
    string Description,
    string? SourceUrl,
    DateTimeOffset? PublishedAt,
    JobPostingStatus Status,
    string? ParsedSummary,
    ImmutableArray<JobPostingSkillSnapshot> Skills,
    uint SourceVersion = 0,
    string SourceInputHash = "",
    string SkillSetHash = "");

public sealed record JobPostingSkillSnapshot(
    Guid Id,
    SkillSnapshot Skill,
    JobSkillRequirementType RequirementType,
    int ImportanceScore,
    bool IsAiDetected);
