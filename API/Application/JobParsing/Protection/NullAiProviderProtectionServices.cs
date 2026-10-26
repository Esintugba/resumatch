using Api.Application.JobParsing.Policies;

namespace Api.Application.JobParsing.Protection;

public sealed class NullAiProviderConcurrencyService : IAiProviderConcurrencyService
{
    public static NullAiProviderConcurrencyService Instance { get; } = new();
    private NullAiProviderConcurrencyService() { }

    public Task<AiProviderConcurrencyAcquireResult> TryAcquireAsync(
        Guid correlationId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderConcurrencyAcquireResult(
            true, new(Guid.NewGuid(), Guid.NewGuid(), expiresAt), null));

    public Task<bool> ReleaseAsync(
        AiProviderConcurrencyLeaseToken token,
        CancellationToken cancellationToken) => Task.FromResult(true);
}

public sealed class NullAiProviderCircuitBreaker : IAiProviderCircuitBreaker
{
    public static NullAiProviderCircuitBreaker Instance { get; } = new();
    private NullAiProviderCircuitBreaker() { }

    public Task<AiProviderCircuitPermitResult> TryAcquirePermissionAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderCircuitPermitResult(true, null, null, true));

    public Task<bool> RecordSuccessAsync(AiProviderCircuitPermitToken token, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    public Task<bool> RecordFailureAsync(AiProviderCircuitPermitToken token, JobParseFailureClassification failure, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    public Task<bool> ReleasePermissionAsync(AiProviderCircuitPermitToken token, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
