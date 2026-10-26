using Api.DTOs.Experience;

namespace Api.Application.Experiences;

public interface IExperienceService
{
    Task<IReadOnlyList<ExperienceResponse>> GetExperiencesAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ExperienceResponse> CreateAsync(
        Guid resumeId,
        CreateExperienceRequest request,
        CancellationToken cancellationToken);

    Task<ExperienceResponse> GetByIdAsync(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken);

    Task<ExperienceResponse> UpdateAsync(
        Guid resumeId,
        Guid experienceId,
        uint expectedVersion,
        UpdateExperienceRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken);
}
