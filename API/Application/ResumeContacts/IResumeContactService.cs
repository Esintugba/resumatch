using Api.DTOs.Resume;

namespace Api.Application.ResumeContacts;

public interface IResumeContactService
{
    Task<ResumeContactResponse> GetAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<ResumeContactUpsertResult> UpsertAsync(
        Guid resumeId,
        ResumeContactRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        CancellationToken cancellationToken);
}
