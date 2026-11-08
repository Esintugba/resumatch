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

    /// <summary>
    /// Archives or unarchives. Already in that state: returns the resume
    /// unchanged, whatever If-Match says. Otherwise If-Match must hold the
    /// current ETag: 428 without, 412 when stale.
    /// </summary>
    Task<ResumeResponse> SetArchivedAsync(
        Guid resumeId,
        bool archived,
        string? ifMatch,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        CancellationToken cancellationToken);
}
