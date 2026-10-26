using Api.DTOs.Project;
using Api.Entities;
using Api.Application.Common.Concurrency;

namespace Api.Application.Mapping;

public static class ProjectMappings
{
    public static ResumeProject ToEntity(this CreateProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeProject
        {
            Name = request.Name,
            Description = request.Description,
            Url = request.Url,
            RepositoryUrl = request.RepositoryUrl,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SortOrder = request.SortOrder,
            Skills = request.SkillIds
                .Select(skillId => new ProjectSkill { SkillId = skillId })
                .ToList()
        };
    }

    public static void ApplyUpdate(
        this UpdateProjectRequest request,
        ResumeProject project)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);

        project.Name = request.Name;
        project.Description = request.Description;
        project.Url = request.Url;
        project.RepositoryUrl = request.RepositoryUrl;
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;
        project.SortOrder = request.SortOrder;
    }

    public static ProjectResponse ToResponse(this ResumeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new ProjectResponse
        {
            Id = project.Id,
            ETag = EntityTagCodec.Format(project.Version),
            Name = project.Name,
            Description = project.Description,
            Url = project.Url,
            RepositoryUrl = project.RepositoryUrl,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            SortOrder = project.SortOrder,
            Skills = project.Skills
                .OrderBy(projectSkill => projectSkill.SkillId)
                .Select(projectSkill => projectSkill.ToResponse())
                .ToList()
        };
    }

    public static ProjectSkillResponse ToResponse(this ProjectSkill projectSkill)
    {
        ArgumentNullException.ThrowIfNull(projectSkill);

        var skill = projectSkill.Skill;

        return new ProjectSkillResponse
        {
            SkillId = projectSkill.SkillId,
            Name = skill?.Name ?? string.Empty,
            Category = skill?.Category ?? default
        };
    }
}
