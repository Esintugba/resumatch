using System.Data;
using System.ComponentModel.DataAnnotations;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Common;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.Resume;
using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Application.Resumes;

public sealed class ResumeService : IResumeService
{
    private const int MaximumPageSize = 100;

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ResumeService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ResumeResponse> CreateAsync(
        CreateResumeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Links.Count > ResumeSectionLimits.Links)
        {
            throw new ValidationException(
                $"A resume cannot contain more than " +
                $"{ResumeSectionLimits.Links} links.");
        }

        var userId = _currentUserService.GetRequiredUserId();
        await ValidateResumeHierarchyAsync(request, userId, cancellationToken);

        var resume = request.ToEntity();
        resume.UserId = userId;
        resume.SourceResumeIsMaster = request.SourceResumeId.HasValue
            ? true
            : null;

        _dbContext.Resumes.Add(resume);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return resume.ToResponse();
    }

    public async Task<ResumeResponse> GetByIdAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId);

        var userId = _currentUserService.GetRequiredUserId();

        var resume = await _dbContext.Resumes
            .AsNoTracking()
            .Include(item => item.Contact)
            .Include(item => item.Links)
            .SingleOrDefaultAsync(
                item => item.Id == resumeId && item.UserId == userId,
                cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        return resume.ToResponse();
    }

    public async Task<ResumeEditorResponse> GetEditorAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId);

        var userId = _currentUserService.GetRequiredUserId();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            "SET TRANSACTION READ ONLY",
            cancellationToken);

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
                item => item.Id == resumeId && item.UserId == userId,
                cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        var response = resume.ToEditorResponse();
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    public async Task<PagedResponse<ResumeListItemResponse>> GetMyResumesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var skip = ValidatePagination(page, pageSize);
        var userId = _currentUserService.GetRequiredUserId();

        var query = _dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);
        var resumes = await query
            .OrderByDescending(resume => resume.UpdatedAt ?? resume.CreatedAt)
            .ThenByDescending(resume => resume.CreatedAt)
            .ThenBy(resume => resume.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<ResumeListItemResponse>
        {
            Items = resumes
                .Select(resume => resume.ToListItemResponse())
                .ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<ResumeResponse> UpdateAsync(
        Guid resumeId,
        uint expectedVersion,
        UpdateResumeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(resumeId);

        var userId = _currentUserService.GetRequiredUserId();

        var resume = await _dbContext.Resumes
            .Include(item => item.Contact)
            .Include(item => item.Links)
            .SingleOrDefaultAsync(
                item => item.Id == resumeId && item.UserId == userId,
                cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        _dbContext.Entry(resume)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        request.ApplyUpdate(resume);
        resume.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return resume.ToResponse();
    }

    public async Task ArchiveAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId);

        var userId = _currentUserService.GetRequiredUserId();

        var resume = await _dbContext.Resumes.SingleOrDefaultAsync(
            item => item.Id == resumeId && item.UserId == userId,
            cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        if (resume.IsArchived)
        {
            return;
        }

        resume.IsArchived = true;
        resume.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId);

        var userId = _currentUserService.GetRequiredUserId();

        var resume = await _dbContext.Resumes.SingleOrDefaultAsync(
            item => item.Id == resumeId && item.UserId == userId,
            cancellationToken);

        if (resume is null)
        {
            throw new NotFoundException("Resume was not found.");
        }

        var hasDerivedResumes = await _dbContext.Resumes
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.SourceResumeId == resumeId &&
                    item.UserId == userId,
                cancellationToken);

        if (hasDerivedResumes)
        {
            throw new ConflictException(
                "Resume cannot be deleted while derived resumes reference it.");
        }

        // JobApplication uses ON DELETE SET NULL, so application history is retained.
        _dbContext.Resumes.Remove(resume);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            throw new ConflictException(
                "Resume cannot be deleted because it is referenced by existing records.",
                exception);
        }
    }

    private async Task ValidateResumeHierarchyAsync(
        CreateResumeRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (request.IsMasterResume && request.SourceResumeId.HasValue)
        {
            throw new ValidationException(
                "A master resume cannot reference a source resume.");
        }

        if (!request.SourceResumeId.HasValue)
        {
            return;
        }

        ValidateRequiredId(request.SourceResumeId.Value);

        var sourceResumeExists = await _dbContext.Resumes
            .AsNoTracking()
            .AnyAsync(
                resume =>
                    resume.Id == request.SourceResumeId.Value &&
                    resume.UserId == userId &&
                    resume.IsMasterResume,
                cancellationToken);

        if (!sourceResumeExists)
        {
            throw new NotFoundException("Source resume was not found.");
        }
    }

    private static void ValidateRequiredId(Guid resumeId)
    {
        if (resumeId == Guid.Empty)
        {
            throw new ValidationException("Resume identifier cannot be empty.");
        }
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

    private static bool IsForeignKeyViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation
        };
    }
}
