using Api.Entities.Enums;

namespace Api.Entities;

public class AiProviderCircuitState : BaseEntity
{
    public string ProviderScope { get; set; } = string.Empty;
    public AiProviderCircuitStatus Status { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? OpenUntil { get; set; }
    public long Generation { get; set; }
    public long NextSequence { get; set; }
    public long LastFailureSequence { get; set; }
    public int ActiveProbes { get; set; }
}
