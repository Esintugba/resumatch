using Api.DTOs.Education;

namespace Api.Application.Educations;

public interface IEducationService
{
    Task<IReadOnlyList<EducationResponse>> GetEducationsAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<EducationResponse> CreateAsync(
        Guid resumeId,
        CreateEducationRequest request,
        CancellationToken cancellationToken);

    Task<EducationResponse> GetByIdAsync(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken);

    Task<EducationResponse> UpdateAsync(
        Guid resumeId,
        Guid educationId,
        UpdateEducationRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken);
}
