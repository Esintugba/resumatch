using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Common.Exceptions;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Analysis.Queries;

public sealed class AnalysisInputLoader
{
    private readonly AppDbContext _dbContext;

    public AnalysisInputLoader(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AnalysisInput> LoadAsync(
        Guid resumeId,
        Guid? jobPostingId,
        Guid currentUserId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(resumeId, jobPostingId, currentUserId);

        if (asOf == default)
        {
            throw new ValidationException(
                "Analysis timestamp must be specified.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            "SET TRANSACTION READ ONLY",
            cancellationToken);

        // Split queries avoid a cartesian product across the resume's many
        // independent collections. REPEATABLE READ keeps those statements on
        // one PostgreSQL snapshot.
        var resume = await _dbContext.Resumes
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Contact)
            .Include(item => item.Links)
            .Include(item => item.Experiences)
                .ThenInclude(experience => experience.Bullets)
            .Include(item => item.Educations)
            .Include(item => item.Skills)
                .ThenInclude(resumeSkill => resumeSkill.Skill)
            .Include(item => item.Projects)
                .ThenInclude(project => project.Skills)
                    .ThenInclude(projectSkill => projectSkill.Skill)
            .Include(item => item.Certifications)
            .Include(item => item.Languages)
            .SingleOrDefaultAsync(
                item => item.Id == resumeId && item.UserId == currentUserId,
                cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        JobPosting? jobPosting = null;
        if (jobPostingId.HasValue)
        {
            jobPosting = await _dbContext.JobPostings
                .AsNoTracking()
                .Include(item => item.Skills.Where(skill =>
                    skill.ProvenanceStatus ==
                        JobPostingSkillProvenanceStatus.Manual ||
                    skill.ProvenanceStatus ==
                        JobPostingSkillProvenanceStatus.AiActive ||
                    skill.ProvenanceStatus ==
                        JobPostingSkillProvenanceStatus.AiManuallyAdopted))
                    .ThenInclude(jobPostingSkill => jobPostingSkill.Skill)
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == jobPostingId.Value &&
                        item.UserId == currentUserId,
                    cancellationToken);

            if (jobPosting is null)
            {
                throw new NotFoundException("Job posting was not found.");
            }
        }

        var input = new AnalysisInput(
            ToSnapshot(resume),
            jobPosting is null ? null : ToSnapshot(jobPosting),
            asOf);

        await transaction.CommitAsync(cancellationToken);
        return input;
    }

    private static ResumeSnapshot ToSnapshot(Resume resume)
    {
        return new ResumeSnapshot(
            resume.Id,
            resume.Title,
            resume.ProfessionalTitle,
            resume.Summary,
            resume.IsMasterResume,
            resume.IsArchived,
            resume.SourceResumeId,
            resume.TemplateKey,
            resume.TargetRole,
            resume.LastExportedAt,
            resume.CreatedAt,
            resume.UpdatedAt,
            resume.Contact is null
                ? null
                : new ResumeContactSnapshot(
                    resume.Contact.Id,
                    resume.Contact.FirstName,
                    resume.Contact.LastName,
                    resume.Contact.Email,
                    resume.Contact.PhoneNumber,
                    resume.Contact.City,
                    resume.Contact.Country,
                    resume.Contact.PostalCode),
            resume.Links
                .OrderBy(link => link.Type)
                .ThenBy(link => link.Id)
                .Select(link => new ResumeLinkSnapshot(
                    link.Id,
                    link.Type,
                    link.Label,
                    link.Url))
                .ToImmutableArray(),
            resume.Experiences
                .OrderBy(experience => experience.SortOrder)
                .ThenBy(experience => experience.Id)
                .Select(experience => new ExperienceSnapshot(
                    experience.Id,
                    experience.CompanyName,
                    experience.Position,
                    experience.Location,
                    experience.EmploymentType,
                    experience.StartDate,
                    experience.EndDate,
                    experience.IsCurrent,
                    experience.Description,
                    experience.SortOrder,
                    experience.Bullets
                        .OrderBy(bullet => bullet.SortOrder)
                        .ThenBy(bullet => bullet.Id)
                        .Select(bullet => new ExperienceBulletSnapshot(
                            bullet.Id,
                            bullet.Content,
                            bullet.SortOrder,
                            bullet.IsAiGenerated,
                            bullet.IsUserApproved,
                            bullet.OriginalContent))
                        .ToImmutableArray()))
                .ToImmutableArray(),
            resume.Educations
                .OrderBy(education => education.SortOrder)
                .ThenBy(education => education.Id)
                .Select(education => new EducationSnapshot(
                    education.Id,
                    education.InstitutionName,
                    education.Degree,
                    education.FieldOfStudy,
                    education.StartDate,
                    education.EndDate,
                    education.Location,
                    education.Grade,
                    education.GradeScale,
                    education.Description,
                    education.SortOrder))
                .ToImmutableArray(),
            resume.Skills
                .OrderBy(resumeSkill => resumeSkill.SortOrder)
                .ThenBy(resumeSkill => resumeSkill.SkillId)
                .ThenBy(resumeSkill => resumeSkill.Id)
                .Select(resumeSkill => new ResumeSkillSnapshot(
                    resumeSkill.Id,
                    ToSnapshot(resumeSkill.Skill),
                    resumeSkill.Level,
                    resumeSkill.SortOrder,
                    resumeSkill.IsHighlighted))
                .ToImmutableArray(),
            resume.Projects
                .OrderBy(project => project.SortOrder)
                .ThenBy(project => project.Id)
                .Select(project => new ProjectSnapshot(
                    project.Id,
                    project.Name,
                    project.Description,
                    project.Url,
                    project.RepositoryUrl,
                    project.StartDate,
                    project.EndDate,
                    project.SortOrder,
                    project.Skills
                        .OrderBy(projectSkill => projectSkill.SkillId)
                        .ThenBy(projectSkill => projectSkill.Id)
                        .Select(projectSkill => new ProjectSkillSnapshot(
                            projectSkill.Id,
                            ToSnapshot(projectSkill.Skill)))
                        .ToImmutableArray()))
                .ToImmutableArray(),
            resume.Certifications
                .OrderBy(certification => certification.SortOrder)
                .ThenBy(certification => certification.Id)
                .Select(certification => new CertificationSnapshot(
                    certification.Id,
                    certification.Name,
                    certification.IssuingOrganization,
                    certification.IssueDate,
                    certification.ExpirationDate,
                    certification.CredentialId,
                    certification.CredentialUrl,
                    certification.SortOrder))
                .ToImmutableArray(),
            resume.Languages
                .OrderBy(language => language.SortOrder)
                .ThenBy(language => language.Id)
                .Select(language => new LanguageSnapshot(
                    language.Id,
                    language.Name,
                    language.Proficiency,
                    language.SortOrder))
                .ToImmutableArray());
    }

    private static JobPostingSnapshot ToSnapshot(JobPosting jobPosting)
    {
        var skills = jobPosting.Skills
            .OrderBy(skill => skill.RequirementType)
            .ThenByDescending(skill => skill.ImportanceScore)
            .ThenBy(skill => skill.SkillId)
            .ThenBy(skill => skill.Id)
            .Select(skill => new JobPostingSkillSnapshot(
                skill.Id,
                ToSnapshot(skill.Skill),
                skill.RequirementType,
                skill.ImportanceScore,
                skill.IsAiDetected))
            .ToImmutableArray();

        return new JobPostingSnapshot(
            jobPosting.Id,
            jobPosting.Title,
            jobPosting.CompanyName,
            jobPosting.Location,
            jobPosting.WorkType,
            jobPosting.EmploymentType,
            jobPosting.Description,
            jobPosting.SourceUrl,
            jobPosting.PublishedAt,
            jobPosting.Status,
            jobPosting.ParsedSummary,
            skills,
            jobPosting.Version,
            SourceInputHashCalculator.Compute(
                jobPosting.Title,
                jobPosting.Description),
            JobPostingSkillSnapshotHashCalculator.Compute(skills));
    }

    private static SkillSnapshot ToSnapshot(Skill skill)
    {
        return new SkillSnapshot(
            skill.Id,
            skill.Name,
            skill.NormalizedName,
            skill.Category);
    }

    private static void ValidateIdentifiers(
        Guid resumeId,
        Guid? jobPostingId,
        Guid currentUserId)
    {
        if (resumeId == Guid.Empty)
        {
            throw new ValidationException(
                "Resume identifier cannot be empty.");
        }

        if (jobPostingId == Guid.Empty)
        {
            throw new ValidationException(
                "Job posting identifier cannot be empty.");
        }

        if (currentUserId == Guid.Empty)
        {
            throw new ValidationException(
                "Current user identifier cannot be empty.");
        }
    }
}
