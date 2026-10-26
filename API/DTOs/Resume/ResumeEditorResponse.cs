using Api.DTOs.Certification;
using Api.DTOs.Education;
using Api.DTOs.Experience;
using Api.DTOs.Language;
using Api.DTOs.Project;
using Api.DTOs.Skill;

namespace Api.DTOs.Resume;

public sealed class ResumeEditorResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? ProfessionalTitle { get; init; }
    public string? Summary { get; init; }
    public bool IsMasterResume { get; init; }
    public Guid? SourceResumeId { get; init; }
    public string? TemplateKey { get; init; }
    public string? TargetRole { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? LastExportedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public ResumeContactResponse? Contact { get; init; }
    public List<ResumeLinkResponse> Links { get; init; } = new();
    public List<ExperienceResponse> Experiences { get; init; } = new();
    public List<EducationResponse> Educations { get; init; } = new();
    public List<ResumeSkillResponse> Skills { get; init; } = new();
    public List<ProjectResponse> Projects { get; init; } = new();
    public List<CertificationResponse> Certifications { get; init; } = new();
    public List<LanguageResponse> Languages { get; init; } = new();
}
