using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeAnalysis : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ResumeId { get; set; }
    public Guid? JobPostingId { get; set; }
    public Guid? JobPostingUserId { get; set; }

    public uint? SourceJobPostingVersion { get; set; }
    public string? SourceJobPostingInputHash { get; set; }
    public string? SourceJobPostingSkillSetHash { get; set; }

    public AnalysisType Type { get; set; }

    public decimal AtsScore { get; set; }
    public decimal? JobMatchScore { get; set; }

    public decimal StructureScore { get; set; }
    public decimal FormattingScore { get; set; }
    public decimal ContentScore { get; set; }
    public decimal KeywordScore { get; set; }

    public string? Summary { get; set; }

    public DateTimeOffset AnalyzedAt { get; set; }

    public string AnalyzerVersion { get; set; } = string.Empty;

    public Resume Resume { get; set; } = null!;
    public JobPosting? JobPosting { get; set; }

    public ICollection<AnalysisIssue> Issues { get; set; } = new List<AnalysisIssue>();
    public ICollection<KeywordMatch> KeywordMatches { get; set; } = new List<KeywordMatch>();
    public ICollection<AnalysisRecommendation> Recommendations { get; set; } = new List<AnalysisRecommendation>();
}
