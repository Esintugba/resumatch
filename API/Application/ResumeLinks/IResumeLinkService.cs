using Api.DTOs.Resume;

namespace Api.Application.ResumeLinks;

public interface IResumeLinkService
{
    Task<IReadOnlyList<ResumeLinkResponse>> GetLinksAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ResumeLinkResponse> CreateAsync(
        Guid resumeId,
        ResumeLinkRequest request,
        CancellationToken cancellationToken);

    Task<ResumeLinkResponse> GetByIdAsync(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken);

    Task<ResumeLinkResponse> UpdateAsync(
        Guid resumeId,
        Guid linkId,
        ResumeLinkRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken);
}
