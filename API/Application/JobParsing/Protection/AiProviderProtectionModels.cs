namespace Api.Application.JobParsing.Protection;

public sealed record AiProviderConcurrencyLeaseToken(
    Guid LeaseId,
    Guid OwnerToken,
    DateTimeOffset ExpiresAt);

public sealed record AiProviderConcurrencyAcquireResult(
    bool Acquired,
    AiProviderConcurrencyLeaseToken? Token,
    DateTimeOffset? RetryAt);

public sealed record AiProviderCircuitPermitToken(
    long Generation,
    long Sequence,
    bool IsHalfOpenProbe,
    Guid? ProbeLeaseId = null,
    Guid? ProbeOwnerToken = null,
    DateTimeOffset? ProbeExpiresAt = null);

public sealed record AiProviderCircuitPermitResult(
    bool Allowed,
    AiProviderCircuitPermitToken? Token,
    DateTimeOffset? RetryAt,
    bool IsDisabled = false);
