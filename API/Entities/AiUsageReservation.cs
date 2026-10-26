using Api.Entities.Enums;

namespace Api.Entities;

public class AiUsageReservation : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid DraftId { get; set; }
    public int AttemptCount { get; set; }
    public string AttemptKey { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public string? ModelName { get; set; }
    public string? ProviderRequestId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int ReservedRequestCount { get; set; }
    public long ReservedInputTokens { get; set; }
    public long ReservedOutputTokens { get; set; }
    public long ReservedTotalTokens { get; set; }
    public AiUsageReservationStatus Status { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ReconciliationRequiredAt { get; set; }
    public AiUsageReconciliationReason? ReconciliationReason { get; set; }
    public Guid? ReconciliationLeaseOwner { get; set; }
    public DateTimeOffset? ReconciliationLeaseExpiresAt { get; set; }
    public long? ActualInputTokens { get; set; }
    public long? ActualOutputTokens { get; set; }
    public long? ActualTotalTokens { get; set; }
    public long OverageInputTokens { get; set; }
    public long OverageOutputTokens { get; set; }
    public long OverageTotalTokens { get; set; }
    public uint Version { get; set; }

    public ICollection<AiUsageReservationPeriod> Periods { get; set; } =
        new List<AiUsageReservationPeriod>();
}
