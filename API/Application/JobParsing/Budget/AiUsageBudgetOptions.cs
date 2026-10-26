namespace Api.Application.JobParsing.Budget;

public sealed class AiUsageBudgetOptions
{
    public const string SectionName = "AiUsageBudget";

    public bool Enabled { get; init; }
    public AiUsageBudgetLimitOptions User { get; init; } = new();
    public AiUsageBudgetLimitOptions System { get; init; } = new();
    public AiUsageReservationOptions Reservation { get; init; } = new();
    public AiUsageReconciliationOptions Reconciliation { get; init; } = new();
}

public sealed class AiUsageBudgetLimitOptions
{
    public long DailyRequestLimit { get; init; } = 100;
    public long DailyTokenLimit { get; init; } = 1_000_000;
    public long MonthlyRequestLimit { get; init; } = 2_000;
    public long MonthlyTokenLimit { get; init; } = 20_000_000;
}

public sealed class AiUsageReservationOptions
{
    public long EstimatedInputTokens { get; init; } = 12_000;
    public long MaximumOutputTokens { get; init; } = 4_096;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(30);
}

public sealed class AiUsageReconciliationOptions
{
    public bool Enabled { get; init; }
    public int BatchSize { get; init; } = 100;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public AiUsageLedgerPurgeOptions Purge { get; init; } = new();
}

public sealed class AiUsageLedgerPurgeOptions
{
    public bool Enabled { get; init; }
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(90);
    public int BatchSize { get; init; } = 100;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromHours(1);
}
