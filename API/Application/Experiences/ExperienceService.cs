using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.Experience;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Experiences;

public sealed class ExperienceService : IExperienceService
{
    private static readonly DateOnly MinimumStartDate = new(1900, 1, 1);

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ExperienceService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<ExperienceResponse>> GetExperiencesAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var experiences = await _dbContext.ResumeExperiences
            .AsNoTracking()
            .Include(experience => experience.Bullets)
            .Where(experience => experience.ResumeId == resumeId)
            .OrderBy(experience => experience.SortOrder)
            .ThenByDescending(experience => experience.StartDate)
            .ThenBy(experience => experience.Id)
            .ToListAsync(cancellationToken);

        return experiences
            .Select(experience => experience.ToResponse())
            .ToList();
    }

    public async Task<ExperienceResponse> CreateAsync(
        Guid resumeId,
        CreateExperienceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateExperienceValues(
            request.EmploymentType,
            request.StartDate,
            request.EndDate,
            request.IsCurrent,
            request.SortOrder);
        ValidateBulletCollection(
            request.Bullets,
            bullet => bullet.SortOrder);

        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeExperiences.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Experiences,
            "experiences",
            cancellationToken);

        var experience = request.ToEntity();
        experience.ResumeId = resumeId;

        _dbContext.ResumeExperiences.Add(experience);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return experience.ToResponse();
    }

    public async Task<ExperienceResponse> GetByIdAsync(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(experienceId, nameof(experienceId));
        var experience = await _dbContext.ResumeExperiences
            .AsNoTracking()
            .Include(item => item.Bullets)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == experienceId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (experience is null)
        {
            throw new NotFoundException("Experience was not found.");
        }

        return experience.ToResponse();
    }

    public async Task<ExperienceResponse> UpdateAsync(
        Guid resumeId,
        Guid experienceId,
        uint expectedVersion,
        UpdateExperienceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(experienceId, nameof(experienceId));
        ValidateExperienceValues(
            request.EmploymentType,
            request.StartDate,
            request.EndDate,
            request.IsCurrent,
            request.SortOrder);
        ValidateBulletCollection(
            request.Bullets,
            bullet => bullet.SortOrder);
        ValidateUpdateBulletIds(request.Bullets);

        var experience = await _dbContext.ResumeExperiences
            .Include(item => item.Bullets)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == experienceId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (experience is null)
        {
            throw new NotFoundException("Experience was not found.");
        }

        _dbContext.Entry(experience)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        ApplyScalarUpdate(request, experience);
        SynchronizeBullets(request.Bullets, experience);
        experience.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return experience.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(experienceId, nameof(experienceId));
        var experience = await _dbContext.ResumeExperiences
            .SingleOrDefaultAsync(
                item =>
                    item.Id == experienceId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (experience is null)
        {
            throw new NotFoundException("Experience was not found.");
        }

        _dbContext.ResumeExperiences.Remove(experience);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyScalarUpdate(
        UpdateExperienceRequest request,
        ResumeExperience experience)
    {
        experience.CompanyName = request.CompanyName;
        experience.Position = request.Position;
        experience.Location = request.Location;
        experience.EmploymentType = request.EmploymentType!.Value;
        experience.StartDate = request.StartDate!.Value;
        experience.EndDate = request.EndDate;
        experience.IsCurrent = request.IsCurrent;
        experience.Description = request.Description;
        experience.SortOrder = request.SortOrder;
    }

    private void SynchronizeBullets(
        IReadOnlyList<UpdateExperienceBulletRequest> requests,
        ResumeExperience experience)
    {
        var existingBulletsById = experience.Bullets
            .ToDictionary(bullet => bullet.Id);
        var retainedBulletIds = new HashSet<Guid>();

        foreach (var request in requests)
        {
            if (!request.Id.HasValue)
            {
                experience.Bullets.Add(request.ToEntity());
                continue;
            }

            if (!existingBulletsById.TryGetValue(request.Id.Value, out var bullet))
            {
                throw new NotFoundException("Experience bullet was not found.");
            }

            retainedBulletIds.Add(bullet.Id);

            var contentChanged = !string.Equals(
                bullet.Content,
                request.Content,
                StringComparison.Ordinal);
            var sortOrderChanged = bullet.SortOrder != request.SortOrder;

            bullet.Content = request.Content;
            bullet.SortOrder = request.SortOrder;

            if (contentChanged && bullet.IsAiGenerated)
            {
                // Preserve AI lineage and the original pre-AI content. A normal user
                // edit is treated as explicit approval of the resulting text.
                bullet.IsUserApproved = true;
            }

            if (contentChanged || sortOrderChanged)
            {
                bullet.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        var removedBullets = experience.Bullets
            .Where(bullet =>
                existingBulletsById.ContainsKey(bullet.Id) &&
                !retainedBulletIds.Contains(bullet.Id))
            .ToList();

        foreach (var removedBullet in removedBullets)
        {
            experience.Bullets.Remove(removedBullet);
            _dbContext.ExperienceBullets.Remove(removedBullet);
        }
    }

    private static void ValidateExperienceValues(
        EmploymentType? employmentType,
        DateOnly? startDate,
        DateOnly? endDate,
        bool isCurrent,
        int sortOrder)
    {
        if (!employmentType.HasValue || !Enum.IsDefined(employmentType.Value))
        {
            throw new ValidationException("The employment type is invalid.");
        }

        var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!startDate.HasValue ||
            startDate.Value < MinimumStartDate ||
            startDate.Value > currentDate)
        {
            throw new ValidationException(
                "Start date must be between 1900-01-01 and the current UTC date.");
        }

        if (endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new ValidationException("End date cannot be earlier than start date.");
        }

        if (isCurrent && endDate.HasValue)
        {
            throw new ValidationException(
                "End date must be empty for a current experience.");
        }

        if (sortOrder < 0)
        {
            throw new ValidationException("Sort order cannot be negative.");
        }
    }

    private static void ValidateBulletCollection<TBullet>(
        IReadOnlyCollection<TBullet>? bullets,
        Func<TBullet, int> getSortOrder)
        where TBullet : class
    {
        if (bullets is null)
        {
            throw new ValidationException("Bullets collection is required.");
        }

        if (bullets.Count > DtoCollectionLimits.ExperienceBullets)
        {
            throw new ValidationException(
                $"An experience cannot contain more than " +
                $"{DtoCollectionLimits.ExperienceBullets} bullets.");
        }

        if (bullets.Any(bullet => bullet is null))
        {
            throw new ValidationException("Bullets cannot contain null values.");
        }

        if (bullets.Any(bullet => getSortOrder(bullet) < 0))
        {
            throw new ValidationException("Bullet sort order cannot be negative.");
        }
    }

    private static void ValidateUpdateBulletIds(
        IReadOnlyCollection<UpdateExperienceBulletRequest> bullets)
    {
        var existingBulletIds = bullets
            .Where(bullet => bullet.Id.HasValue)
            .Select(bullet => bullet.Id!.Value)
            .ToList();

        if (existingBulletIds.Any(id => id == Guid.Empty))
        {
            throw new ValidationException(
                "Experience bullet identifiers cannot be empty GUIDs.");
        }

        if (existingBulletIds.Distinct().Count() != existingBulletIds.Count)
        {
            throw new ValidationException(
                "Duplicate experience bullet identifiers are not allowed.");
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
