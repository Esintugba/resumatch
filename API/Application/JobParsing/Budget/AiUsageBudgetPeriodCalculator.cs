using Api.Entities.Enums;

namespace Api.Application.JobParsing.Budget;

public readonly record struct AiUsageBudgetPeriodWindow(
    AiUsageBudgetPeriodType PeriodType,
    DateTimeOffset Start,
    DateTimeOffset End);

public static class AiUsageBudgetPeriodCalculator
{
    public static AiUsageBudgetPeriodWindow GetDaily(DateTimeOffset asOf)
    {
        var utc = asOf.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return new(AiUsageBudgetPeriodType.Daily, start, start.AddDays(1));
    }

    public static AiUsageBudgetPeriodWindow GetMonthly(DateTimeOffset asOf)
    {
        var utc = asOf.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new(AiUsageBudgetPeriodType.Monthly, start, start.AddMonths(1));
    }
}
