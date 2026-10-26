using System.ComponentModel.DataAnnotations;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.JobApplication;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Application.JobApplications;

public sealed class JobApplicationService : IJobApplicationService
{
    private const int MaximumPageSize = 100;
    private const string UniqueUserJobPostingIndexName =
        "IX_JobApplications_UserId_JobPostingId";
    private const string JobPostingOwnershipForeignKeyName =
        "FK_JobApplications_JobPostings_JobPostingId_UserId";
    private const string ResumeOwnershipForeignKeyName =
        "FK_JobApplications_Resumes_ResumeId_ResumeUserId";
    private const string DuplicateApplicationMessage =
        "A job application already exists for this job posting.";
    private const string OwnershipReferenceConflictMessage =
        "The selected job posting or resume is no longer available. " +
        "Reload and retry.";

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public JobApplicationService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<JobApplicationResponse> CreateAsync(
        CreateJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(request.JobPostingId, nameof(request.JobPostingId));
        ValidateOptionalId(request.ResumeId, nameof(request.ResumeId));

        var initialStatus = request.AppliedAt.HasValue
            ? ApplicationStatus.Applied
            : ApplicationStatus.Preparing;
        ValidateTimeline(
            initialStatus,
            request.AppliedAt,
            request.NextActionAt);

        var userId = _currentUserService.GetRequiredUserId();
        var jobPosting = await _dbContext.JobPostings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == request.JobPostingId &&
                    item.UserId == userId,
                cancellationToken);

        if (jobPosting is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        Resume? resume = null;
        if (request.ResumeId.HasValue)
        {
            resume = await GetOwnedResumeAsync(
                request.ResumeId.Value,
                userId,
                cancellationToken);
        }

        var alreadyExists = await _dbContext.JobApplications
            .AsNoTracking()
            .AnyAsync(
                application =>
                    application.UserId == userId &&
                    application.JobPostingId == request.JobPostingId,
                cancellationToken);

        if (alreadyExists)
        {
            throw new ConflictException(DuplicateApplicationMessage);
        }

        var application = request.ToEntity();
        application.UserId = userId;
        application.JobPostingId = request.JobPostingId;
        application.ResumeId = request.ResumeId;
        application.ResumeUserId = request.ResumeId.HasValue
            ? userId
            : null;
        application.Status = initialStatus;

        _dbContext.JobApplications.Add(application);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateApplication(exception))
        {
            throw new ConflictException(DuplicateApplicationMessage, exception);
        }
        catch (DbUpdateException exception) when (IsOwnershipReferenceViolation(exception))
        {
            throw new ConflictException(
                OwnershipReferenceConflictMessage,
                exception);
        }

        application.JobPosting = jobPosting;
        application.Resume = resume;
        return application.ToResponse();
    }

    public async Task<JobApplicationResponse> GetByIdAsync(
        Guid jobApplicationId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(jobApplicationId, nameof(jobApplicationId));
        var userId = _currentUserService.GetRequiredUserId();

        var application = await _dbContext.JobApplications
            .AsNoTracking()
            .Include(item => item.JobPosting)
            .Include(item => item.Resume)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == jobApplicationId &&
                    item.UserId == userId,
                cancellationToken);

        if (application is null)
        {
            throw new NotFoundException("Job application was not found.");
        }

        return application.ToResponse();
    }

    public async Task<PagedResponse<JobApplicationListItemResponse>>
        GetMyJobApplicationsAsync(
            int page,
            int pageSize,
            ApplicationStatus? status,
            Guid? jobPostingId,
            CancellationToken cancellationToken)
    {
        var skip = ValidatePagination(page, pageSize);
        ValidateOptionalId(jobPostingId, nameof(jobPostingId));

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new ValidationException("The application status is invalid.");
        }

        var userId = _currentUserService.GetRequiredUserId();
        var query = _dbContext.JobApplications
            .AsNoTracking()
            .Where(item => item.UserId == userId);

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        if (jobPostingId.HasValue)
        {
            query = query.Where(
                item => item.JobPostingId == jobPostingId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var applications = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.Version,
                item.JobPostingId,
                JobTitle = item.JobPosting.Title,
                CompanyName = item.JobPosting.CompanyName,
                item.ResumeId,
                ResumeTitle = item.Resume == null ? null : item.Resume.Title,
                item.Status,
                item.AppliedAt,
                item.NextActionAt,
                item.CreatedAt,
                item.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<JobApplicationListItemResponse>
        {
            Items = applications
                .Select(item => new JobApplicationListItemResponse
                {
                    Id = item.Id,
                    ETag = EntityTagCodec.Format(item.Version),
                    JobPostingId = item.JobPostingId,
                    JobTitle = item.JobTitle,
                    CompanyName = item.CompanyName,
                    ResumeId = item.ResumeId,
                    ResumeTitle = item.ResumeTitle,
                    Status = item.Status,
                    AppliedAt = item.AppliedAt,
                    NextActionAt = item.NextActionAt,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                })
                .ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<JobApplicationResponse> UpdateAsync(
        Guid jobApplicationId,
        uint expectedVersion,
        UpdateJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(jobApplicationId, nameof(jobApplicationId));
        ValidateOptionalId(request.ResumeId, nameof(request.ResumeId));

        if (!request.Status.HasValue || !Enum.IsDefined(request.Status.Value))
        {
            throw new ValidationException("The application status is invalid.");
        }

        var userId = _currentUserService.GetRequiredUserId();
        var application = await _dbContext.JobApplications
            .Include(item => item.JobPosting)
            .Include(item => item.Resume)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == jobApplicationId &&
                    item.UserId == userId,
                cancellationToken);

        if (application is null)
        {
            throw new NotFoundException("Job application was not found.");
        }

        _dbContext.Entry(application)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        var targetStatus = request.Status.Value;
        if (!ApplicationStatusTransitionPolicy.CanTransition(
                application.Status,
                targetStatus))
        {
            throw new ConflictException(
                $"Application status cannot transition from " +
                $"{application.Status} to {targetStatus}.");
        }

        var appliedAt = ResolveAppliedAt(
            application.Status,
            targetStatus,
            application.AppliedAt,
            request.AppliedAt);

        ValidateTimeline(
            targetStatus,
            appliedAt,
            request.NextActionAt);

        var resumeChanged = application.ResumeId != request.ResumeId;
        Resume? selectedResume = application.Resume;

        if (resumeChanged && request.ResumeId.HasValue)
        {
            selectedResume = await GetOwnedResumeAsync(
                request.ResumeId.Value,
                userId,
                cancellationToken);
        }
        else if (resumeChanged)
        {
            selectedResume = null;
        }

        request.ApplyUpdate(application);
        application.AppliedAt = appliedAt;
        application.ResumeUserId = request.ResumeId.HasValue
            ? userId
            : null;
        application.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOwnershipReferenceViolation(exception))
        {
            throw new ConflictException(
                OwnershipReferenceConflictMessage,
                exception);
        }

        if (resumeChanged)
        {
            application.Resume = selectedResume;
        }

        return application.ToResponse();
    }

    private async Task<Resume> GetOwnedResumeAsync(
        Guid resumeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var resume = await _dbContext.Resumes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == resumeId && item.UserId == userId,
                cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        return resume;
    }

    private static DateTimeOffset? ResolveAppliedAt(
        ApplicationStatus currentStatus,
        ApplicationStatus targetStatus,
        DateTimeOffset? currentAppliedAt,
        DateTimeOffset? requestedAppliedAt)
    {
        if (currentAppliedAt.HasValue)
        {
            if (!requestedAppliedAt.HasValue ||
                requestedAppliedAt.Value != currentAppliedAt.Value)
            {
                throw new ValidationException(
                    "Applied date cannot be changed after the application is submitted.");
            }

            return currentAppliedAt;
        }

        if (!requestedAppliedAt.HasValue)
        {
            return null;
        }

        var isFirstSubmission =
            currentStatus is ApplicationStatus.Wishlist or ApplicationStatus.Preparing &&
            targetStatus == ApplicationStatus.Applied;

        if (!isFirstSubmission)
        {
            throw new ValidationException(
                "Applied date can only be set when the application first transitions " +
                "to Applied.");
        }

        return requestedAppliedAt;
    }

    private static void ValidateTimeline(
        ApplicationStatus status,
        DateTimeOffset? appliedAt,
        DateTimeOffset? nextActionAt)
    {
        if (appliedAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new ValidationException("Applied date cannot be in the future.");
        }

        if (status is ApplicationStatus.Wishlist or ApplicationStatus.Preparing &&
            appliedAt.HasValue)
        {
            throw new ValidationException(
                "Applied date must be empty before the application is submitted.");
        }

        if (StatusRequiresAppliedDate(status) && !appliedAt.HasValue)
        {
            throw new ValidationException(
                "Applied date is required for the selected application status.");
        }

        if (appliedAt.HasValue &&
            nextActionAt.HasValue &&
            nextActionAt.Value < appliedAt.Value)
        {
            throw new ValidationException(
                "Next action date cannot be earlier than the applied date.");
        }

        if (status is ApplicationStatus.Rejected or
            ApplicationStatus.Withdrawn or
            ApplicationStatus.Accepted &&
            nextActionAt.HasValue)
        {
            throw new ValidationException(
                "Next action date must be empty for a terminal application status.");
        }
    }

    private static bool StatusRequiresAppliedDate(ApplicationStatus status)
    {
        return status is
            ApplicationStatus.Applied or
            ApplicationStatus.Screening or
            ApplicationStatus.Interview or
            ApplicationStatus.Offer or
            ApplicationStatus.Rejected or
            ApplicationStatus.Accepted;
    }

    private static int ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ValidationException("Page must be greater than zero.");
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ValidationException(
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            throw new ValidationException(
                "The requested page is outside the supported range.");
        }

        return (int)skip;
    }

    private static void ValidateRequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException(
                $"{parameterName} cannot be an empty GUID.");
        }
    }

    private static void ValidateOptionalId(Guid? id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException(
                $"{parameterName} cannot be an empty GUID.");
        }
    }

    private static bool IsDuplicateApplication(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueUserJobPostingIndexName
        };
    }

    private static bool IsOwnershipReferenceViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation
            } postgresException)
        {
            return false;
        }

        return postgresException.ConstraintName is
            JobPostingOwnershipForeignKeyName or
            ResumeOwnershipForeignKeyName;
    }
}
