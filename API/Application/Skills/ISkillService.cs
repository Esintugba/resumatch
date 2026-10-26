using Api.DTOs.Skill;
using Api.Entities.Enums;

namespace Api.Application.Skills;

public interface ISkillService
{
    Task<IReadOnlyList<SkillResponse>> GetSkillsAsync(
        string? search = null,
        SkillCategory? category = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResumeSkillResponse>> GetResumeSkillsAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ResumeSkillResponse> AddResumeSkillAsync(
        Guid resumeId,
        AddResumeSkillRequest request,
        CancellationToken cancellationToken);

    Task<ResumeSkillResponse> UpdateResumeSkillAsync(
        Guid resumeId,
        Guid skillId,
        UpdateResumeSkillRequest request,
        CancellationToken cancellationToken);

    Task DeleteResumeSkillAsync(
        Guid resumeId,
        Guid skillId,
        CancellationToken cancellationToken);
}
