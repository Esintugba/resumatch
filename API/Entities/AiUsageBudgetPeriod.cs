using Api.Entities.Enums;

namespace Api.Entities;

public class AiUsageBudgetPeriod : BaseEntity
{
    public AiUsageBudgetScopeType ScopeType { get; set; }
    public Guid? UserId { get; set; }
    public AiUsageBudgetPeriodType PeriodType { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public long RequestLimit { get; set; }
    public long TokenLimit { get; set; }
    public long ReservedRequests { get; set; }
    public long ReservedTokens { get; set; }
    public long ConsumedRequests { get; set; }
    public long ConsumedInputTokens { get; set; }
    public long ConsumedOutputTokens { get; set; }
    public long ConsumedTotalTokens { get; set; }

    public ICollection<AiUsageReservationPeriod> Reservations { get; set; } =
        new List<AiUsageReservationPeriod>();
}
