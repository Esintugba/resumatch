using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Skill;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Skills;

public sealed class SkillService : ISkillService
{
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 150;

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public SkillService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<SkillResponse>> GetSkillsAsync(
        string? search = null,
        SkillCategory? category = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var skip = ValidatePagination(page, pageSize);

        if (category.HasValue && !Enum.IsDefined(category.Value))
        {
            throw new ValidationException("The skill category is invalid.");
        }

        IQueryable<Entities.Skill> query = _dbContext.Skills.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();

            if (normalizedSearch.Length > MaximumSearchLength)
            {
                throw new ValidationException(
                    $"Search cannot exceed {MaximumSearchLength} characters.");
            }

            query = query.Where(skill =>
                skill.NormalizedName.Contains(normalizedSearch));
        }

        if (category.HasValue)
        {
            query = query.Where(skill => skill.Category == category.Value);
        }

        var skills = await query
            .OrderBy(skill => skill.NormalizedName)
            .ThenBy(skill => skill.Category)
            .ThenBy(skill => skill.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return skills
            .Select(skill => skill.ToResponse())
            .ToList();
    }

    public async Task<IReadOnlyList<ResumeSkillResponse>> GetResumeSkillsAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId, nameof(resumeId));

        var userId = _currentUserService.GetRequiredUserId();
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var resumeSkills = await _dbContext.ResumeSkills
            .AsNoTracking()
            .Include(resumeSkill => resumeSkill.Skill)
            .Where(resumeSkill => resumeSkill.ResumeId == resumeId)
            .OrderBy(resumeSkill => resumeSkill.SortOrder)
            .ThenBy(resumeSkill => resumeSkill.Skill.NormalizedName)
            .ThenBy(resumeSkill => resumeSkill.SkillId)
            .ToListAsync(cancellationToken);

        return resumeSkills
            .Select(resumeSkill => resumeSkill.ToResponse())
            .ToList();
    }

    public async Task<ResumeSkillResponse> AddResumeSkillAsync(
        Guid resumeId,
        AddResumeSkillRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(request.SkillId, nameof(request.SkillId));
        ValidateResumeSkillValues(request.SortOrder, request.Level);

        var userId = _currentUserService.GetRequiredUserId();
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeSkills.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Skills,
            "skills",
            cancellationToken);

        var skill = await _dbContext.Skills
            .AsNoTracking()
            .SingleOrDefaultAsync(
                skill => skill.Id == request.SkillId,
                cancellationToken);

        if (skill is null)
        {
            throw new NotFoundException("Skill was not found.");
        }

        var alreadyExists = await _dbContext.ResumeSkills
            .AsNoTracking()
            .AnyAsync(
                resumeSkill =>
                    resumeSkill.ResumeId == resumeId &&
                    resumeSkill.SkillId == request.SkillId,
                cancellationToken);

        if (alreadyExists)
        {
            throw new ConflictException(
                "The skill is already added to this resume.",
                ApiErrorCodes.DuplicateSkill);
        }

        var resumeSkill = request.ToEntity();
        resumeSkill.ResumeId = resumeId;

        _dbContext.ResumeSkills.Add(resumeSkill);
        await _dbContext.SaveChangesAsync(cancellationToken);

        resumeSkill.Skill = skill;
        return resumeSkill.ToResponse();
    }

    public async Task<ResumeSkillResponse> UpdateResumeSkillAsync(
        Guid resumeId,
        Guid skillId,
        UpdateResumeSkillRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(skillId, nameof(skillId));
        ValidateResumeSkillValues(request.SortOrder, request.Level);

        var userId = _currentUserService.GetRequiredUserId();

        var resumeSkill = await _dbContext.ResumeSkills
            .Include(item => item.Skill)
            .SingleOrDefaultAsync(
                item =>
                    item.ResumeId == resumeId &&
                    item.SkillId == skillId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (resumeSkill is null)
        {
            throw new NotFoundException("Resume skill was not found.");
        }

        request.ApplyUpdate(resumeSkill);
        resumeSkill.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return resumeSkill.ToResponse();
    }

    public async Task DeleteResumeSkillAsync(
        Guid resumeId,
        Guid skillId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(skillId, nameof(skillId));

        var userId = _currentUserService.GetRequiredUserId();

        var resumeSkill = await _dbContext.ResumeSkills
            .SingleOrDefaultAsync(
                item =>
                    item.ResumeId == resumeId &&
                    item.SkillId == skillId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (resumeSkill is null)
        {
            throw new NotFoundException("Resume skill was not found.");
        }

        _dbContext.ResumeSkills.Remove(resumeSkill);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
            throw new ValidationException("The requested page is outside the supported range.");
        }

        return (int)skip;
    }

    private static void ValidateRequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException($"{parameterName} cannot be an empty GUID.");
        }
    }

    private static void ValidateResumeSkillValues(
        int sortOrder,
        SkillLevel? level)
    {
        if (sortOrder < 0)
        {
            throw new ValidationException("Sort order cannot be negative.");
        }

        if (level.HasValue && !Enum.IsDefined(level.Value))
        {
            throw new ValidationException("The skill level is invalid.");
        }
    }
}
