using Api.Entities.Enums;

namespace Api.Application.JobParsing.Budget;

public sealed class NullAiUsageBudgetService : IAiUsageBudgetService
{
    public static NullAiUsageBudgetService Instance { get; } = new();

    private NullAiUsageBudgetService()
    {
    }

    public Task<AiUsageReservationResult> ReserveAsync(
        AiUsageReservationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiUsageReservationResult(AiUsageReservationOutcome.Disabled));

    public Task<AiUsageSettlementResult> SettleAsync(
        AiUsageSettlementRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiUsageSettlementResult(AiUsageReservationStatus.Released, false));

    public Task<AiUsageSettlementResult> ReleaseAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiUsageSettlementResult(AiUsageReservationStatus.Released, false));

    public Task<AiUsageSettlementResult> MarkReconciliationRequiredAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiUsageSettlementResult(
            AiUsageReservationStatus.ReconciliationRequired, false));

    public Task<AiUsageSettlementResult> ReconcileAsync(
        AiUsageReconciliationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiUsageSettlementResult(
            AiUsageReservationStatus.ReconciliationRequired, false));
}
