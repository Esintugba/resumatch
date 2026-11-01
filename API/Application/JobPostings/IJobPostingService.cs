using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities.Enums;

namespace Api.Application.JobPostings;

public interface IJobPostingService
{
    Task<JobPostingResponse> CreateAsync(
        CreateJobPostingRequest request,
        CancellationToken cancellationToken);

    Task<JobPostingResponse> GetByIdAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken);

    Task<PagedResponse<JobPostingListItemResponse>> GetMyJobPostingsAsync(
        int page,
        int pageSize,
        string? search,
        JobPostingStatus? status,
        CancellationToken cancellationToken);

    Task<JobPostingResponse> UpdateAsync(
        Guid jobPostingId,
        uint expectedVersion,
        UpdateJobPostingRequest request,
        CancellationToken cancellationToken);

    Task<JobPostingResponse> AddSkillAsync(
        Guid jobPostingId,
        uint expectedVersion,
        AddJobPostingSkillRequest request,
        CancellationToken cancellationToken);

    Task<JobPostingResponse> RemoveSkillAsync(
        Guid jobPostingId,
        Guid skillId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    Task<JobPostingResponse> AdoptSkillAsync(
        Guid jobPostingId,
        Guid skillId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid jobPostingId,
        uint expectedVersion,
        CancellationToken cancellationToken);
}
