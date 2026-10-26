using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record KeywordMatchResult
{
    public KeywordMatchResult(
        Guid? skillId,
        string keyword,
        KeywordMatchStatus status,
        decimal weight,
        string? resumeSection)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Keyword is required.", nameof(keyword));
        }

        if (weight is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                "Keyword weight must be between 0 and 100.");
        }

        SkillId = skillId;
        Keyword = keyword;
        Status = status;
        Weight = weight;
        ResumeSection = resumeSection;
    }

    public Guid? SkillId { get; }
    public string Keyword { get; }
    public KeywordMatchStatus Status { get; }
    public decimal Weight { get; }
    public string? ResumeSection { get; }
}
