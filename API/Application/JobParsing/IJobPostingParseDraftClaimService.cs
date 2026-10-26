using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;

namespace Api.Application.JobParsing;

public interface IJobPostingParseDraftClaimService
{
    Task<JobPostingParseDraftClaim?> ClaimNextAsync(
        string leaseOwner,
        CancellationToken cancellationToken);

    Task<JobPostingParseDraftClaim> RenewLeaseAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken);

    Task ReleaseForRetryAsync(
        JobPostingParseDraftClaim claim,
        JobParseFailureClassification failure,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    Task ReleaseForBudgetWaitAsync(
        JobPostingParseDraftClaim claim,
        string failureCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task ReleaseForInfrastructureWaitAsync(
        JobPostingParseDraftClaim claim,
        string failureCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task MarkFailedAsync(
        JobPostingParseDraftClaim claim,
        JobParseFailureClassification failure,
        CancellationToken cancellationToken);
}
