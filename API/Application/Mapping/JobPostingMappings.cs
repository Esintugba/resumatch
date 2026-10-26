using Api.Application.Common.Concurrency;
using Api.DTOs.JobPosting;
using Api.Entities;

namespace Api.Application.Mapping;

public static class JobPostingMappings
{
    public static JobPosting ToEntity(this CreateJobPostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new JobPosting
        {
            Title = request.Title,
            CompanyName = request.CompanyName,
            Location = request.Location,
            WorkType = request.WorkType,
            EmploymentType = request.EmploymentType,
            Description = request.Description,
            SourceUrl = request.SourceUrl,
            PublishedAt = request.PublishedAt
        };
    }

    public static void ApplyUpdate(
        this UpdateJobPostingRequest request,
        JobPosting jobPosting)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(jobPosting);

        jobPosting.Title = request.Title;
        jobPosting.CompanyName = request.CompanyName;
        jobPosting.Location = request.Location;
        jobPosting.WorkType = request.WorkType;
        jobPosting.EmploymentType = request.EmploymentType;
        jobPosting.Description = request.Description;
        jobPosting.SourceUrl = request.SourceUrl;
        jobPosting.PublishedAt = request.PublishedAt;
    }

    public static JobPostingResponse ToResponse(this JobPosting jobPosting)
    {
        ArgumentNullException.ThrowIfNull(jobPosting);

        return new JobPostingResponse
        {
            Id = jobPosting.Id,
            ETag = EntityTagCodec.Format(jobPosting.Version),
            Title = jobPosting.Title,
            CompanyName = jobPosting.CompanyName,
            Location = jobPosting.Location,
            WorkType = jobPosting.WorkType,
            EmploymentType = jobPosting.EmploymentType,
            Description = jobPosting.Description,
            SourceUrl = jobPosting.SourceUrl,
            PublishedAt = jobPosting.PublishedAt,
            Status = jobPosting.Status,
            ParsedSummary = jobPosting.ParsedSummary,
            Skills = jobPosting.Skills
                .OrderByDescending(skill => skill.ImportanceScore)
                .ThenBy(skill => skill.SkillId)
                .Select(skill => skill.ToResponse())
                .ToList(),
            CreatedAt = jobPosting.CreatedAt
        };
    }

    public static JobPostingListItemResponse ToListItemResponse(this JobPosting jobPosting)
    {
        ArgumentNullException.ThrowIfNull(jobPosting);

        return new JobPostingListItemResponse
        {
            Id = jobPosting.Id,
            ETag = EntityTagCodec.Format(jobPosting.Version),
            Title = jobPosting.Title,
            CompanyName = jobPosting.CompanyName,
            Location = jobPosting.Location,
            WorkType = jobPosting.WorkType,
            EmploymentType = jobPosting.EmploymentType,
            PublishedAt = jobPosting.PublishedAt,
            Status = jobPosting.Status,
            CreatedAt = jobPosting.CreatedAt
        };
    }

    public static JobPostingSkillResponse ToResponse(this JobPostingSkill jobPostingSkill)
    {
        ArgumentNullException.ThrowIfNull(jobPostingSkill);

        var skill = jobPostingSkill.Skill;

        return new JobPostingSkillResponse
        {
            SkillId = jobPostingSkill.SkillId,
            SkillName = skill?.Name ?? string.Empty,
            Category = skill?.Category ?? default,
            RequirementType = jobPostingSkill.RequirementType,
            ImportanceScore = jobPostingSkill.ImportanceScore,
            IsAiDetected = jobPostingSkill.IsAiDetected,
            ProvenanceStatus = jobPostingSkill.ProvenanceStatus,
            SourceParseDraftId = jobPostingSkill.SourceParseDraftId,
            SourceParseDraftSkillId = jobPostingSkill.SourceParseDraftSkillId,
            InvalidatedAt = jobPostingSkill.InvalidatedAt
        };
    }
}
