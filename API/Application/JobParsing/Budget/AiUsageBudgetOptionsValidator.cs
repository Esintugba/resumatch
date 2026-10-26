using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing.Budget;

public sealed class AiUsageBudgetOptionsValidator :
    IValidateOptions<AiUsageBudgetOptions>
{
    public ValidateOptionsResult Validate(string? name, AiUsageBudgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        ValidateLimits(options.User, "User", failures);
        ValidateLimits(options.System, "System", failures);

        if (options.Reservation.EstimatedInputTokens <= 0 ||
            options.Reservation.MaximumOutputTokens <= 0)
        {
            failures.Add("Reservation token amounts must be positive.");
        }
        else
        {
            try
            {
                _ = checked(options.Reservation.EstimatedInputTokens +
                    options.Reservation.MaximumOutputTokens);
            }
            catch (OverflowException)
            {
                failures.Add("Reservation token amounts overflow Int64.");
            }
        }

        if (options.Reservation.Lifetime < TimeSpan.FromMinutes(1) ||
            options.Reservation.Lifetime > TimeSpan.FromDays(1))
        {
            failures.Add("Reservation Lifetime must be between one minute and one day.");
        }

        if (options.Reconciliation.BatchSize is < 1 or > 1_000)
        {
            failures.Add("Reconciliation BatchSize must be between 1 and 1000.");
        }

        if (options.Reconciliation.PollInterval < TimeSpan.FromMinutes(1) ||
            options.Reconciliation.PollInterval > TimeSpan.FromDays(1))
        {
            failures.Add("Reconciliation PollInterval must be between one minute and one day.");
        }

        if (options.Reconciliation.MinimumAge < TimeSpan.Zero ||
            options.Reconciliation.MinimumAge > TimeSpan.FromDays(7))
        {
            failures.Add("Reconciliation MinimumAge must be between zero and seven days.");
        }

        if (options.Reconciliation.LeaseDuration < TimeSpan.FromMinutes(1) ||
            options.Reconciliation.LeaseDuration > TimeSpan.FromHours(1))
        {
            failures.Add("Reconciliation LeaseDuration must be between one minute and one hour.");
        }

        if (options.Reconciliation.Purge.RetentionPeriod < TimeSpan.FromDays(1) ||
            options.Reconciliation.Purge.RetentionPeriod > TimeSpan.FromDays(3650))
        {
            failures.Add("Ledger purge RetentionPeriod must be between one day and ten years.");
        }

        if (options.Reconciliation.Purge.BatchSize is < 1 or > 1_000)
        {
            failures.Add("Ledger purge BatchSize must be between 1 and 1000.");
        }

        if (options.Reconciliation.Purge.PollInterval < TimeSpan.FromMinutes(1) ||
            options.Reconciliation.Purge.PollInterval > TimeSpan.FromDays(1))
        {
            failures.Add("Ledger purge PollInterval must be between one minute and one day.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateLimits(
        AiUsageBudgetLimitOptions limits,
        string scope,
        ICollection<string> failures)
    {
        if (limits.DailyRequestLimit <= 0 || limits.DailyTokenLimit <= 0 ||
            limits.MonthlyRequestLimit <= 0 || limits.MonthlyTokenLimit <= 0)
        {
            failures.Add($"{scope} budget limits must be positive.");
        }

        if (limits.MonthlyRequestLimit < limits.DailyRequestLimit ||
            limits.MonthlyTokenLimit < limits.DailyTokenLimit)
        {
            failures.Add($"{scope} monthly limits cannot be lower than daily limits.");
        }
    }
}
