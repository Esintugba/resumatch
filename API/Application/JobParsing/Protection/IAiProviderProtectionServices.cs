using Api.Application.JobParsing.Policies;

namespace Api.Application.JobParsing.Protection;

public interface IAiProviderConcurrencyService
{
    Task<AiProviderConcurrencyAcquireResult> TryAcquireAsync(
        Guid correlationId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(
        AiProviderConcurrencyLeaseToken token,
        CancellationToken cancellationToken);
}

public interface IAiProviderCircuitBreaker
{
    Task<AiProviderCircuitPermitResult> TryAcquirePermissionAsync(
        CancellationToken cancellationToken);

    Task<bool> RecordSuccessAsync(
        AiProviderCircuitPermitToken token,
        CancellationToken cancellationToken);

    Task<bool> RecordFailureAsync(
        AiProviderCircuitPermitToken token,
        JobParseFailureClassification failure,
        CancellationToken cancellationToken);

    Task<bool> ReleasePermissionAsync(
        AiProviderCircuitPermitToken token,
        CancellationToken cancellationToken);
}
