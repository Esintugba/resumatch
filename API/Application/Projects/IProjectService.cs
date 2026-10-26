using Api.DTOs.Project;

namespace Api.Application.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectResponse>> GetProjectsAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ProjectResponse> CreateAsync(
        Guid resumeId,
        CreateProjectRequest request,
        CancellationToken cancellationToken);

    Task<ProjectResponse> GetByIdAsync(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectResponse> UpdateAsync(
        Guid resumeId,
        Guid projectId,
        uint expectedVersion,
        UpdateProjectRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken);
}
