using Api.Application.Common.Concurrency;
using Api.DTOs.JobApplication;
using Api.Entities;

namespace Api.Application.Mapping;

public static class JobApplicationMappings
{
    public static JobApplication ToEntity(this CreateJobApplicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new JobApplication
        {
            JobPostingId = request.JobPostingId,
            ResumeId = request.ResumeId,
            AppliedAt = request.AppliedAt,
            Notes = request.Notes,
            NextActionAt = request.NextActionAt
        };
    }

    public static void ApplyUpdate(
        this UpdateJobApplicationRequest request,
        JobApplication application)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(application);

        application.Status = request.Status!.Value;
        application.ResumeId = request.ResumeId;
        application.Notes = request.Notes;
        application.NextActionAt = request.NextActionAt;
    }

    public static JobApplicationResponse ToResponse(this JobApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var jobPosting = application.JobPosting;
        var resume = application.Resume;

        return new JobApplicationResponse
        {
            Id = application.Id,
            ETag = EntityTagCodec.Format(application.Version),
            JobPostingId = application.JobPostingId,
            JobTitle = jobPosting?.Title ?? string.Empty,
            CompanyName = jobPosting?.CompanyName,
            ResumeId = application.ResumeId,
            ResumeTitle = resume?.Title,
            Status = application.Status,
            AppliedAt = application.AppliedAt,
            Notes = application.Notes,
            NextActionAt = application.NextActionAt,
            CreatedAt = application.CreatedAt,
            UpdatedAt = application.UpdatedAt
        };
    }

    public static JobApplicationListItemResponse ToListItemResponse(
        this JobApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var jobPosting = application.JobPosting;
        var resume = application.Resume;

        return new JobApplicationListItemResponse
        {
            Id = application.Id,
            ETag = EntityTagCodec.Format(application.Version),
            JobPostingId = application.JobPostingId,
            JobTitle = jobPosting?.Title ?? string.Empty,
            CompanyName = jobPosting?.CompanyName,
            ResumeId = application.ResumeId,
            ResumeTitle = resume?.Title,
            Status = application.Status,
            AppliedAt = application.AppliedAt,
            NextActionAt = application.NextActionAt,
            CreatedAt = application.CreatedAt,
            UpdatedAt = application.UpdatedAt
        };
    }
}
