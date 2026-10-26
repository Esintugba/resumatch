using System.Collections.Immutable;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record ResumeSnapshot(
    Guid Id,
    string Title,
    string? ProfessionalTitle,
    string? Summary,
    bool IsMasterResume,
    bool IsArchived,
    Guid? SourceResumeId,
    string? TemplateKey,
    string? TargetRole,
    DateTimeOffset? LastExportedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    ResumeContactSnapshot? Contact,
    ImmutableArray<ResumeLinkSnapshot> Links,
    ImmutableArray<ExperienceSnapshot> Experiences,
    ImmutableArray<EducationSnapshot> Educations,
    ImmutableArray<ResumeSkillSnapshot> Skills,
    ImmutableArray<ProjectSnapshot> Projects,
    ImmutableArray<CertificationSnapshot> Certifications,
    ImmutableArray<LanguageSnapshot> Languages);

public sealed record ResumeContactSnapshot(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string? City,
    string? Country,
    string? PostalCode);

public sealed record ResumeLinkSnapshot(
    Guid Id,
    ResumeLinkType Type,
    string? Label,
    string Url);

public sealed record ExperienceSnapshot(
    Guid Id,
    string CompanyName,
    string Position,
    string? Location,
    EmploymentType EmploymentType,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description,
    int SortOrder,
    ImmutableArray<ExperienceBulletSnapshot> Bullets);

public sealed record ExperienceBulletSnapshot(
    Guid Id,
    string Content,
    int SortOrder,
    bool IsAiGenerated,
    bool IsUserApproved,
    string? OriginalContent);

public sealed record EducationSnapshot(
    Guid Id,
    string InstitutionName,
    string? Degree,
    string? FieldOfStudy,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Location,
    decimal? Grade,
    string? GradeScale,
    string? Description,
    int SortOrder);

public sealed record ResumeSkillSnapshot(
    Guid Id,
    SkillSnapshot Skill,
    SkillLevel? Level,
    int SortOrder,
    bool IsHighlighted);

public sealed record ProjectSnapshot(
    Guid Id,
    string Name,
    string? Description,
    string? Url,
    string? RepositoryUrl,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int SortOrder,
    ImmutableArray<ProjectSkillSnapshot> Skills);

public sealed record ProjectSkillSnapshot(
    Guid Id,
    SkillSnapshot Skill);

public sealed record CertificationSnapshot(
    Guid Id,
    string Name,
    string? IssuingOrganization,
    DateOnly? IssueDate,
    DateOnly? ExpirationDate,
    string? CredentialId,
    string? CredentialUrl,
    int SortOrder);

public sealed record LanguageSnapshot(
    Guid Id,
    string Name,
    LanguageProficiency Proficiency,
    int SortOrder);
