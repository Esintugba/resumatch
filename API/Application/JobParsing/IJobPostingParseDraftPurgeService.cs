namespace Api.Application.JobParsing;

public interface IJobPostingParseDraftPurgeService
{
    Task<int> PurgeBatchAsync(CancellationToken cancellationToken);
}
