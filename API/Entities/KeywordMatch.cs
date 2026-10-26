using Api.Entities.Enums;

namespace Api.Entities;

public class KeywordMatch : BaseEntity
{
    public Guid ResumeAnalysisId { get; set; }

    public Guid? SkillId { get; set; }

    public string Keyword { get; set; } = string.Empty;

    public KeywordMatchStatus Status { get; set; }

    public decimal Weight { get; set; }

    public string? ResumeSection { get; set; }

    public ResumeAnalysis ResumeAnalysis { get; set; } = null!;
    public Skill? Skill { get; set; }
}
