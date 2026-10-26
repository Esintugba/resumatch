using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Api.Application.JobPostings;

public sealed class JobPostingService : IJobPostingService
{
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 250;
    private const string LikeEscapeCharacter = "~";
    private const string DeleteConflictMessage =
        "Job posting cannot be deleted while job applications reference it.";
    private const string ManualSkillAdoptionConflictMessage =
        "Manual job posting skills cannot be adopted from AI provenance.";
    private const string LegacySkillAdoptionConflictMessage =
        "Legacy AI skill provenance must be reconfirmed before manual adoption.";

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public JobPostingService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<JobPostingResponse> CreateAsync(
        CreateJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEditableValues(request.Title, request.Description);

        var userId = _currentUserService.GetRequiredUserId();
        var jobPosting = request.ToEntity();

        jobPosting.UserId = userId;
        jobPosting.Status = JobPostingStatus.Draft;
        jobPosting.ParsedSummary = null;

        _dbContext.JobPostings.Add(jobPosting);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return jobPosting.ToResponse();
    }

    public async Task<JobPostingResponse> GetByIdAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(jobPostingId);
        var userId = _currentUserService.GetRequiredUserId();

        var jobPosting = await _dbContext.JobPostings
            .AsNoTracking()
            .Include(item => item.Skills)
                .ThenInclude(jobPostingSkill => jobPostingSkill.Skill)
            .SingleOrDefaultAsync(
                item => item.Id == jobPostingId && item.UserId == userId,
                cancellationToken);

        if (jobPosting is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        return jobPosting.ToResponse();
    }

    public async Task<PagedResponse<JobPostingListItemResponse>> GetMyJobPostingsAsync(
        int page,
        int pageSize,
        string? search,
        JobPostingStatus? status,
        CancellationToken cancellationToken)
    {
        var skip = ValidatePagination(page, pageSize);
        var searchTerm = NormalizeSearch(search);

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new ValidationException("The job posting status is invalid.");
        }

        var userId = _currentUserService.GetRequiredUserId();
        var query = _dbContext.JobPostings
            .AsNoTracking()
            .Where(item => item.UserId == userId);

        if (searchTerm is not null)
        {
            var pattern = $"%{EscapeLikePattern(searchTerm)}%";
            query = query.Where(item =>
                EF.Functions.ILike(
                    item.Title,
                    pattern,
                    LikeEscapeCharacter) ||
                (item.CompanyName != null &&
                 EF.Functions.ILike(
                     item.CompanyName,
                     pattern,
                     LikeEscapeCharacter)));
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var jobPostings = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.Version,
                item.Title,
                item.CompanyName,
                item.Location,
                item.WorkType,
                item.EmploymentType,
                item.PublishedAt,
                item.Status,
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<JobPostingListItemResponse>
        {
            Items = jobPostings
                .Select(item => new JobPostingListItemResponse
                {
                    Id = item.Id,
                    ETag = EntityTagCodec.Format(item.Version),
                    Title = item.Title,
                    CompanyName = item.CompanyName,
                    Location = item.Location,
                    WorkType = item.WorkType,
                    EmploymentType = item.EmploymentType,
                    PublishedAt = item.PublishedAt,
                    Status = item.Status,
                    CreatedAt = item.CreatedAt
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

    public async Task<JobPostingResponse> UpdateAsync(
        Guid jobPostingId,
        uint expectedVersion,
        UpdateJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(jobPostingId);
        ValidateEditableValues(request.Title, request.Description);

        var userId = _currentUserService.GetRequiredUserId();
        var updatedAt = _timeProvider.GetUtcNow().ToUniversalTime();
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var currentVersion = await LockOwnedJobPostingAsync(
            transaction,
            jobPostingId,
            userId,
            cancellationToken);
        if (currentVersion is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        if (currentVersion.Value != expectedVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The job posting was modified by another operation.");
        }

        var jobPosting = await _dbContext.JobPostings
            .Include(item => item.Skills)
                .ThenInclude(jobPostingSkill => jobPostingSkill.Skill)
            .SingleOrDefaultAsync(
                item => item.Id == jobPostingId && item.UserId == userId,
                cancellationToken);

        if (jobPosting is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        _dbContext.Entry(jobPosting)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        var currentSourceHash = SourceInputHashCalculator.Compute(
            jobPosting.Title,
            jobPosting.Description);
        var requestedSourceHash = SourceInputHashCalculator.Compute(
            request.Title,
            request.Description);
        var sourceChanged = !string.Equals(
            currentSourceHash,
            requestedSourceHash,
            StringComparison.Ordinal);

        request.ApplyUpdate(jobPosting);
        jobPosting.UpdatedAt = updatedAt;

        if (sourceChanged)
        {
            foreach (var skill in jobPosting.Skills.Where(item =>
                         item.ProvenanceStatus ==
                         JobPostingSkillProvenanceStatus.AiActive))
            {
                skill.ProvenanceStatus = JobPostingSkillProvenanceStatus.AiStale;
                skill.InvalidatedAt = updatedAt;
                skill.UpdatedAt = updatedAt;
            }

            jobPosting.ParsedSummary = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobPosting.ToResponse();
    }

    public async Task<JobPostingResponse> AdoptSkillAsync(
        Guid jobPostingId,
        Guid skillId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(jobPostingId);
        ValidateRequiredSkillId(skillId);

        var userId = _currentUserService.GetRequiredUserId();
        var updatedAt = _timeProvider.GetUtcNow().ToUniversalTime();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var currentVersion = await LockOwnedJobPostingAsync(
            transaction,
            jobPostingId,
            userId,
            cancellationToken);
        if (currentVersion is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        if (currentVersion.Value != expectedVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The job posting was modified by another operation.");
        }

        var jobPosting = await _dbContext.JobPostings
            .Include(item => item.Skills)
                .ThenInclude(jobPostingSkill => jobPostingSkill.Skill)
            .SingleOrDefaultAsync(
                item => item.Id == jobPostingId && item.UserId == userId,
                cancellationToken);

        if (jobPosting is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        _dbContext.Entry(jobPosting)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        var jobPostingSkill = jobPosting.Skills.SingleOrDefault(
            item => item.SkillId == skillId);
        if (jobPostingSkill is null)
        {
            throw new NotFoundException("Job posting skill was not found.");
        }

        switch (jobPostingSkill.ProvenanceStatus)
        {
            case JobPostingSkillProvenanceStatus.AiManuallyAdopted:
                await transaction.CommitAsync(cancellationToken);
                return jobPosting.ToResponse();

            case JobPostingSkillProvenanceStatus.Manual:
                throw new ConflictException(ManualSkillAdoptionConflictMessage);

            case JobPostingSkillProvenanceStatus.LegacyAiUnknown:
                throw new ConflictException(LegacySkillAdoptionConflictMessage);

            case JobPostingSkillProvenanceStatus.AiActive:
            case JobPostingSkillProvenanceStatus.AiStale:
                EnsureSourceProvenanceIsComplete(jobPostingSkill);
                break;

            default:
                throw new ConflictException(
                    "The job posting skill provenance cannot be manually adopted.");
        }

        jobPostingSkill.ProvenanceStatus =
            JobPostingSkillProvenanceStatus.AiManuallyAdopted;
        jobPostingSkill.InvalidatedAt = null;
        jobPostingSkill.UpdatedAt = updatedAt;
        jobPosting.UpdatedAt = updatedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobPosting.ToResponse();
    }

    private async Task<uint?> LockOwnedJobPostingAsync(
        IDbContextTransaction transaction,
        Guid jobPostingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT posting.xmin
            FROM "JobPostings" AS posting
            WHERE posting."Id" = @jobPostingId
              AND posting."UserId" = @userId
            FOR UPDATE;
            """;
        AddParameter(command, "jobPostingId", jobPostingId);
        AddParameter(command, "userId", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (uint)result;
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task DeleteAsync(
        Guid jobPostingId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(jobPostingId);
        var userId = _currentUserService.GetRequiredUserId();

        var jobPosting = await _dbContext.JobPostings.SingleOrDefaultAsync(
            item => item.Id == jobPostingId && item.UserId == userId,
            cancellationToken);

        if (jobPosting is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        _dbContext.Entry(jobPosting)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        var hasApplications = await _dbContext.JobApplications
            .AsNoTracking()
            .AnyAsync(
                application => application.JobPostingId == jobPostingId,
                cancellationToken);

        if (hasApplications)
        {
            throw new ConflictException(DeleteConflictMessage);
        }

        _dbContext.JobPostings.Remove(jobPosting);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            throw new ConflictException(DeleteConflictMessage, exception);
        }
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var searchTerm = search.Trim();
        if (searchTerm.Length > MaximumSearchLength)
        {
            throw new ValidationException(
                $"Search cannot exceed {MaximumSearchLength} characters.");
        }

        return searchTerm;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal);
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

    private static void ValidateEditableValues(string title, string description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ValidationException("Job posting title is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationException("Job posting description is required.");
        }
    }

    private static void ValidateRequiredId(Guid jobPostingId)
    {
        if (jobPostingId == Guid.Empty)
        {
            throw new ValidationException(
                "Job posting identifier cannot be empty.");
        }
    }

    private static void ValidateRequiredSkillId(Guid skillId)
    {
        if (skillId == Guid.Empty)
        {
            throw new ValidationException("Skill identifier cannot be empty.");
        }
    }

    private static void EnsureSourceProvenanceIsComplete(
        JobPostingSkill jobPostingSkill)
    {
        if (jobPostingSkill.SourceParseDraftId is null ||
            jobPostingSkill.SourceParseDraftSkillId is null)
        {
            throw new ConflictException(
                "The AI skill source provenance is incomplete and cannot be adopted.");
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation
        };
    }
}
