using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Budget;

public static class AiUsageProviderUsagePolicy
{
    public static bool TryValidate(
        ProviderTokenUsage? usage,
        out long input,
        out long output,
        out long total)
    {
        input = usage?.InputTokens ?? -1;
        output = usage?.OutputTokens ?? -1;
        total = usage?.TotalTokens ?? -1;
        return input >= 0 && output >= 0 && total >= 0 &&
            input <= long.MaxValue - output && input + output == total;
    }
}

public static class AiUsageReservationTransitionPolicy
{
    public static bool CanTransition(
        AiUsageReservationStatus current,
        AiUsageReservationStatus target) =>
        current switch
        {
            AiUsageReservationStatus.Reserved =>
                target is AiUsageReservationStatus.Settled or
                    AiUsageReservationStatus.Released or
                    AiUsageReservationStatus.Expired or
                    AiUsageReservationStatus.ReconciliationRequired,
            AiUsageReservationStatus.ReconciliationRequired or
                AiUsageReservationStatus.Expired =>
                target == AiUsageReservationStatus.Settled,
            _ => false
        };
}

public static class AiUsageLedgerRetentionPolicy
{
    public static bool CanPurge(
        AiUsageReservationStatus status,
        DateTimeOffset? settledAt,
        DateTimeOffset? releasedAt,
        DateTimeOffset cutoff) =>
        status switch
        {
            AiUsageReservationStatus.Settled => settledAt <= cutoff,
            AiUsageReservationStatus.Released => releasedAt <= cutoff,
            _ => false
        };
}

public readonly record struct AiUsageOverage(
    long InputTokens,
    long OutputTokens,
    long TotalTokens)
{
    public static AiUsageOverage Calculate(
        long reservedInput,
        long reservedOutput,
        long reservedTotal,
        long actualInput,
        long actualOutput,
        long actualTotal)
    {
        if (reservedInput < 0 || reservedOutput < 0 || reservedTotal < 0 ||
            actualInput < 0 || actualOutput < 0 || actualTotal < 0 ||
            reservedInput > long.MaxValue - reservedOutput ||
            actualInput > long.MaxValue - actualOutput ||
            reservedInput + reservedOutput != reservedTotal ||
            actualInput + actualOutput != actualTotal)
        {
            throw new ArgumentOutOfRangeException(nameof(actualTotal));
        }

        return new(
            Math.Max(actualInput - reservedInput, 0),
            Math.Max(actualOutput - reservedOutput, 0),
            Math.Max(actualTotal - reservedTotal, 0));
    }
}
