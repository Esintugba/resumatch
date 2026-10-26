using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing;

public interface IJobPostingParseDraftExecutionService
{
    Task ExecuteAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken);
}
