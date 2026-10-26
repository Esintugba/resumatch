using Api.Entities.Enums;

namespace Api.DTOs.Analysis;

public sealed class KeywordMatchResponse
{
    public Guid Id { get; init; }
    public Guid? SkillId { get; init; }
    public string Keyword { get; init; } = string.Empty;
    public KeywordMatchStatus Status { get; init; }
    public decimal Weight { get; init; }
    public string? ResumeSection { get; init; }
}
