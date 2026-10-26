using Api.DTOs.JobPosting;
using Api.DTOs.Common;
using Api.Entities.Enums;

namespace Api.Application.JobParsing;

public interface IJobPostingParseDraftService
{
    Task<JobPostingParseDraftResponse> RequestParseAsync(
        Guid jobPostingId,
        uint expectedJobPostingVersion,
        CancellationToken cancellationToken);

    Task<JobPostingParseDraftDetailResponse> GetDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        CancellationToken cancellationToken);

    Task<PagedResponse<JobPostingParseDraftListItemResponse>> GetDraftHistoryAsync(
        Guid jobPostingId,
        int page,
        int pageSize,
        JobPostingParseDraftStatus? status,
        CancellationToken cancellationToken);

    Task<JobPostingParseDraftResponse> CancelDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        uint expectedDraftVersion,
        CancellationToken cancellationToken);

    Task<JobPostingParseDraftResponse> ConfirmDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        uint expectedDraftVersion,
        ConfirmJobPostingParseDraftRequest request,
        CancellationToken cancellationToken);
}
