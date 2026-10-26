namespace Api.Entities;

public class AiUsageReservationPeriod
{
    public Guid AiUsageReservationId { get; set; }
    public Guid AiUsageBudgetPeriodId { get; set; }
    public int ReservedRequestCount { get; set; }
    public long ReservedTokenCount { get; set; }

    public AiUsageReservation Reservation { get; set; } = null!;
    public AiUsageBudgetPeriod BudgetPeriod { get; set; } = null!;
}
