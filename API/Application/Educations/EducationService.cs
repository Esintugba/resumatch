using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Education;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Educations;

public sealed class EducationService : IEducationService
{
    private const decimal MaximumGradeMagnitude = 999.99m;

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public EducationService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<EducationResponse>> GetEducationsAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var educations = await _dbContext.ResumeEducations
            .AsNoTracking()
            .Where(education => education.ResumeId == resumeId)
            .OrderBy(education => education.SortOrder)
            .ThenByDescending(education => education.EndDate)
            .ThenByDescending(education => education.StartDate)
            .ThenBy(education => education.Id)
            .ToListAsync(cancellationToken);

        return educations
            .Select(education => education.ToResponse())
            .ToList();
    }

    public async Task<EducationResponse> CreateAsync(
        Guid resumeId,
        CreateEducationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateEducationValues(
            request.StartDate,
            request.EndDate,
            request.Grade,
            request.SortOrder);

        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeEducations.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Educations,
            "education records",
            cancellationToken);

        var education = request.ToEntity();
        education.ResumeId = resumeId;

        _dbContext.ResumeEducations.Add(education);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return education.ToResponse();
    }

    public async Task<EducationResponse> GetByIdAsync(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(educationId, nameof(educationId));
        var education = await _dbContext.ResumeEducations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == educationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (education is null)
        {
            throw new NotFoundException("Education was not found.");
        }

        return education.ToResponse();
    }

    public async Task<EducationResponse> UpdateAsync(
        Guid resumeId,
        Guid educationId,
        UpdateEducationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(educationId, nameof(educationId));
        ValidateEducationValues(
            request.StartDate,
            request.EndDate,
            request.Grade,
            request.SortOrder);

        var education = await _dbContext.ResumeEducations
            .SingleOrDefaultAsync(
                item =>
                    item.Id == educationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (education is null)
        {
            throw new NotFoundException("Education was not found.");
        }

        request.ApplyUpdate(education);
        education.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return education.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(educationId, nameof(educationId));
        var education = await _dbContext.ResumeEducations
            .SingleOrDefaultAsync(
                item =>
                    item.Id == educationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (education is null)
        {
            throw new NotFoundException("Education was not found.");
        }

        _dbContext.ResumeEducations.Remove(education);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateEducationValues(
        DateOnly? startDate,
        DateOnly? endDate,
        decimal? grade,
        int sortOrder)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new ValidationException("End date cannot be earlier than start date.");
        }

        if (grade.HasValue &&
            (grade.Value < -MaximumGradeMagnitude ||
             grade.Value > MaximumGradeMagnitude ||
             decimal.Round(grade.Value, 2) != grade.Value))
        {
            throw new ValidationException(
                "Grade must have at most three integer digits and two decimal digits.");
        }

        if (sortOrder < 0)
        {
            throw new ValidationException("Sort order cannot be negative.");
        }
    }

    private static void ValidateRequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException($"{parameterName} cannot be an empty GUID.");
        }
    }
}
