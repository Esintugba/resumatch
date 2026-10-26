using Api.DTOs.Common;
using Api.DTOs.Resume;

namespace Api.Application.Resumes;

public interface IResumeService
{
    Task<ResumeResponse> CreateAsync(
        CreateResumeRequest request,
        CancellationToken cancellationToken);

    Task<ResumeResponse> GetByIdAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ResumeEditorResponse> GetEditorAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<PagedResponse<ResumeListItemResponse>> GetMyResumesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ResumeResponse> UpdateAsync(
        Guid resumeId,
        uint expectedVersion,
        UpdateResumeRequest request,
        CancellationToken cancellationToken);

    Task ArchiveAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        CancellationToken cancellationToken);
}
