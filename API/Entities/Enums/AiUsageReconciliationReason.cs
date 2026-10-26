namespace Api.Entities.Enums;

public enum AiUsageReconciliationReason
{
    MissingUsage = 0,
    UnknownProviderOutcome = 1,
    ExpiredUnknownOutcome = 2,
    ActualExceedsReserved = 3
}
