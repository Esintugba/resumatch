using Api.Entities.Enums;

namespace Api.Entities;

public class AiProviderConcurrencyLease : BaseEntity
{
    public string ProviderScope { get; set; } = string.Empty;
    public Guid OwnerToken { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public AiProviderConcurrencyLeaseStatus Status { get; set; }
}
