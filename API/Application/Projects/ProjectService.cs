using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.Project;
using Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Projects;

public sealed class ProjectService : IProjectService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ProjectService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<ProjectResponse>> GetProjectsAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var projects = await _dbContext.ResumeProjects
            .AsNoTracking()
            .Include(project => project.Skills)
            .ThenInclude(projectSkill => projectSkill.Skill)
            .Where(project => project.ResumeId == resumeId)
            .OrderBy(project => project.SortOrder)
            .ThenByDescending(project => project.EndDate)
            .ThenByDescending(project => project.StartDate)
            .ThenBy(project => project.Id)
            .ToListAsync(cancellationToken);

        return projects
            .Select(project => project.ToResponse())
            .ToList();
    }

    public async Task<ProjectResponse> CreateAsync(
        Guid resumeId,
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateProjectValues(
            request.StartDate,
            request.EndDate,
            request.SortOrder,
            request.SkillIds);

        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeProjects.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Projects,
            "projects",
            cancellationToken);
        var skillsById = await GetRequiredSkillsAsync(
            request.SkillIds,
            cancellationToken);

        var project = request.ToEntity();
        project.ResumeId = resumeId;

        _dbContext.ResumeProjects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        SetSkillNavigations(project.Skills, skillsById);
        return project.ToResponse();
    }

    public async Task<ProjectResponse> GetByIdAsync(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(projectId, nameof(projectId));
        var project = await _dbContext.ResumeProjects
            .AsNoTracking()
            .Include(item => item.Skills)
            .ThenInclude(projectSkill => projectSkill.Skill)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == projectId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (project is null)
        {
            throw new NotFoundException("Project was not found.");
        }

        return project.ToResponse();
    }

    public async Task<ProjectResponse> UpdateAsync(
        Guid resumeId,
        Guid projectId,
        uint expectedVersion,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(projectId, nameof(projectId));
        ValidateProjectValues(
            request.StartDate,
            request.EndDate,
            request.SortOrder,
            request.SkillIds);

        var project = await _dbContext.ResumeProjects
            .Include(item => item.Skills)
            .ThenInclude(projectSkill => projectSkill.Skill)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == projectId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (project is null)
        {
            throw new NotFoundException("Project was not found.");
        }

        var skillsById = await GetRequiredSkillsAsync(
            request.SkillIds,
            cancellationToken);

        _dbContext.Entry(project)
            .Property(item => item.Version)
            .OriginalValue = expectedVersion;

        ApplyScalarUpdate(request, project);
        var addedProjectSkills = SynchronizeSkills(request.SkillIds, project);
        project.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        SetSkillNavigations(addedProjectSkills, skillsById);
        return project.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(projectId, nameof(projectId));
        var project = await _dbContext.ResumeProjects
            .SingleOrDefaultAsync(
                item =>
                    item.Id == projectId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (project is null)
        {
            throw new NotFoundException("Project was not found.");
        }

        _dbContext.ResumeProjects.Remove(project);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, Skill>> GetRequiredSkillsAsync(
        IReadOnlyCollection<Guid> skillIds,
        CancellationToken cancellationToken)
    {
        if (skillIds.Count == 0)
        {
            return new Dictionary<Guid, Skill>();
        }

        var skills = await _dbContext.Skills
            .AsNoTracking()
            .Where(skill => skillIds.Contains(skill.Id))
            .ToListAsync(cancellationToken);

        if (skills.Count != skillIds.Count)
        {
            throw new NotFoundException("One or more skills were not found.");
        }

        return skills.ToDictionary(skill => skill.Id);
    }

    private static void ApplyScalarUpdate(
        UpdateProjectRequest request,
        ResumeProject project)
    {
        project.Name = request.Name;
        project.Description = request.Description;
        project.Url = request.Url;
        project.RepositoryUrl = request.RepositoryUrl;
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;
        project.SortOrder = request.SortOrder;
    }

    private List<ProjectSkill> SynchronizeSkills(
        IReadOnlyCollection<Guid> requestedSkillIds,
        ResumeProject project)
    {
        var requested = requestedSkillIds.ToHashSet();
        var existing = project.Skills
            .Select(projectSkill => projectSkill.SkillId)
            .ToHashSet();

        var toRemove = project.Skills
            .Where(projectSkill => !requested.Contains(projectSkill.SkillId))
            .OrderBy(projectSkill => projectSkill.SkillId)
            .ToList();

        var toAdd = requested
            .Except(existing)
            .OrderBy(skillId => skillId)
            .Select(skillId => new ProjectSkill
            {
                ResumeProjectId = project.Id,
                SkillId = skillId
            })
            .ToList();

        foreach (var projectSkill in toRemove)
        {
            project.Skills.Remove(projectSkill);
            _dbContext.ProjectSkills.Remove(projectSkill);
        }

        foreach (var projectSkill in toAdd)
        {
            project.Skills.Add(projectSkill);
        }

        return toAdd;
    }

    private static void SetSkillNavigations(
        IEnumerable<ProjectSkill> projectSkills,
        IReadOnlyDictionary<Guid, Skill> skillsById)
    {
        foreach (var projectSkill in projectSkills)
        {
            projectSkill.Skill = skillsById[projectSkill.SkillId];
        }
    }

    private static void ValidateProjectValues(
        DateOnly? startDate,
        DateOnly? endDate,
        int sortOrder,
        IReadOnlyCollection<Guid>? skillIds)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new ValidationException("End date cannot be earlier than start date.");
        }

        if (sortOrder < 0)
        {
            throw new ValidationException("Sort order cannot be negative.");
        }

        if (skillIds is null)
        {
            throw new ValidationException("Skill identifiers collection is required.");
        }

        if (skillIds.Count > DtoCollectionLimits.ProjectSkills)
        {
            throw new ValidationException(
                $"A project cannot contain more than " +
                $"{DtoCollectionLimits.ProjectSkills} skills.");
        }

        if (skillIds.Any(skillId => skillId == Guid.Empty))
        {
            throw new ValidationException("Skill identifiers cannot be empty GUIDs.");
        }

        if (skillIds.Distinct().Count() != skillIds.Count)
        {
            throw new ValidationException("Duplicate skill identifiers are not allowed.");
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
