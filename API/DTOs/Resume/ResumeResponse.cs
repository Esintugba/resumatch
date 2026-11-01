namespace Api.DTOs.Resume;

public sealed class ResumeResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? ProfessionalTitle { get; init; }
    public string? Summary { get; init; }
    public string? UnlistedSkillsNote { get; init; }
    public bool IsMasterResume { get; init; }
    public Guid? SourceResumeId { get; init; }
    public string? TemplateKey { get; init; }
    public string? TargetRole { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public ResumeContactResponse? Contact { get; init; }
    public List<ResumeLinkResponse> Links { get; init; } = new();
}
