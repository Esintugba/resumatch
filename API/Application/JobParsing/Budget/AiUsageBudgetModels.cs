using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Budget;

public enum AiUsageReservationOutcome
{
    Disabled,
    Reserved,
    Duplicate,
    UserBudgetExceeded,
    SystemBudgetExceeded
}

public sealed record AiUsageReservationRequest(
    Guid UserId,
    Guid DraftId,
    int AttemptCount);

public sealed record AiUsageReservationResult(
    AiUsageReservationOutcome Outcome,
    Guid? ReservationId = null,
    DateTimeOffset? NextEligibleAt = null)
{
    public bool MayCallProvider =>
        Outcome is AiUsageReservationOutcome.Disabled or
            AiUsageReservationOutcome.Reserved;
}

public sealed record AiUsageSettlementRequest(
    Guid ReservationId,
    ProviderTokenUsage? Usage,
    string? ProviderName,
    string? ModelName,
    string? ProviderRequestId = null);

public sealed record AiUsageSettlementResult(
    AiUsageReservationStatus Status,
    bool StateChanged);

public sealed record AiUsageReconciliationClaim(
    Guid ReservationId,
    uint Version,
    AiUsageReservationStatus Status,
    AiUsageReconciliationReason Reason,
    DateTimeOffset RequiredAt,
    DateTimeOffset LeaseExpiresAt);

public sealed record AiUsageReconciliationRequest(
    Guid ReservationId,
    uint ExpectedVersion,
    ProviderTokenUsage Usage,
    string? ProviderName = null,
    string? ModelName = null,
    string? ProviderRequestId = null);

public readonly record struct AiUsageReservationAmount(
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens)
{
    public static AiUsageReservationAmount Create(long inputTokens, long outputTokens)
    {
        if (inputTokens <= 0 || outputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        }

        return new(1, inputTokens, outputTokens, checked(inputTokens + outputTokens));
    }
}
