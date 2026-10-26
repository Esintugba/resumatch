using Api.Entities.Enums;

namespace Api.Entities;

public class AiProviderCircuitProbeLease : BaseEntity
{
    public string ProviderScope { get; set; } = string.Empty;
    public long Generation { get; set; }
    public long Sequence { get; set; }
    public Guid OwnerToken { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public AiProviderCircuitProbeStatus Status { get; set; }
}
