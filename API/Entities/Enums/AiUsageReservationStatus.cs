namespace Api.Entities.Enums;

public enum AiUsageReservationStatus
{
    Reserved = 0,
    Settled = 1,
    Released = 2,
    Expired = 3,
    ReconciliationRequired = 4
}
