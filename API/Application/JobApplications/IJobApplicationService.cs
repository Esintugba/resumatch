using Api.DTOs.Common;
using Api.DTOs.JobApplication;
using Api.Entities.Enums;

namespace Api.Application.JobApplications;

public interface IJobApplicationService
{
    Task<JobApplicationResponse> CreateAsync(
        CreateJobApplicationRequest request,
        CancellationToken cancellationToken);

    Task<JobApplicationResponse> GetByIdAsync(
        Guid jobApplicationId,
        CancellationToken cancellationToken);

    Task<PagedResponse<JobApplicationListItemResponse>> GetMyJobApplicationsAsync(
        int page,
        int pageSize,
        ApplicationStatus? status,
        Guid? jobPostingId,
        CancellationToken cancellationToken);

    Task<JobApplicationResponse> UpdateAsync(
        Guid jobApplicationId,
        uint expectedVersion,
        UpdateJobApplicationRequest request,
        CancellationToken cancellationToken);
}
