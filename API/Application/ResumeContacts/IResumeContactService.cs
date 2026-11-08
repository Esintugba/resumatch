using Api.DTOs.Resume;

namespace Api.Application.ResumeContacts;

public interface IResumeContactService
{
    Task<ResumeContactResponse> GetAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the contact when there is none and no If-Match was sent;
    /// otherwise updates it only if If-Match holds its current ETag.
    /// </summary>
    Task<ResumeContactUpsertResult> UpsertAsync(
        Guid resumeId,
        string? ifMatch,
        ResumeContactRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        string? ifMatch,
        CancellationToken cancellationToken);
}
