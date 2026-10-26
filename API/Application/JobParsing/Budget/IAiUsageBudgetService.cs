namespace Api.Application.JobParsing.Budget;

public interface IAiUsageBudgetService
{
    Task<AiUsageReservationResult> ReserveAsync(
        AiUsageReservationRequest request,
        CancellationToken cancellationToken);

    Task<AiUsageSettlementResult> SettleAsync(
        AiUsageSettlementRequest request,
        CancellationToken cancellationToken);

    Task<AiUsageSettlementResult> ReleaseAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    Task<AiUsageSettlementResult> MarkReconciliationRequiredAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    Task<AiUsageSettlementResult> ReconcileAsync(
        AiUsageReconciliationRequest request,
        CancellationToken cancellationToken);
}

public interface IAiUsageReservationReconciliationService
{
    Task<int> ReconcileExpiredReservationsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AiUsageReconciliationClaim>> ClaimBatchAsync(
        Guid leaseOwner,
        CancellationToken cancellationToken);

    Task<bool> ReleaseClaimAsync(
        AiUsageReconciliationClaim claim,
        Guid leaseOwner,
        CancellationToken cancellationToken);

    Task<int> PurgeResolvedReservationsAsync(
        CancellationToken cancellationToken);
}
