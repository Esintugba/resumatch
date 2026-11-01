using Api.DTOs.Skill;
using Api.Entities;

namespace Api.Application.Mapping;

public static class SkillMappings
{
    public static ResumeSkill ToEntity(this AddResumeSkillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeSkill
        {
            SkillId = request.SkillId,
            Level = request.Level,
            SortOrder = request.SortOrder,
            IsHighlighted = request.IsHighlighted
        };
    }

    public static void ApplyUpdate(
        this UpdateResumeSkillRequest request,
        ResumeSkill resumeSkill)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resumeSkill);

        resumeSkill.Level = request.Level;
        resumeSkill.SortOrder = request.SortOrder;
        resumeSkill.IsHighlighted = request.IsHighlighted;
    }

    public static SkillResponse ToResponse(this Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        return new SkillResponse
        {
            Id = skill.Id,
            Name = skill.Name,
            NameTr = skill.NameTr,
            Category = skill.Category
        };
    }

    public static ResumeSkillResponse ToResponse(this ResumeSkill resumeSkill)
    {
        ArgumentNullException.ThrowIfNull(resumeSkill);

        var skill = resumeSkill.Skill;

        return new ResumeSkillResponse
        {
            SkillId = resumeSkill.SkillId,
            Name = skill?.Name ?? string.Empty,
            Category = skill?.Category ?? default,
            Level = resumeSkill.Level,
            SortOrder = resumeSkill.SortOrder,
            IsHighlighted = resumeSkill.IsHighlighted
        };
    }
}
