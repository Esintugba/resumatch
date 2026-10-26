using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftOptionsValidator :
    IValidateOptions<JobPostingParseDraftOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        JobPostingParseDraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.ParseRequestsPerWindow is < 1 or > 1_000)
        {
            failures.Add("ParseRequestsPerWindow must be between 1 and 1000.");
        }

        if (options.ParseRateLimitWindow < TimeSpan.FromSeconds(1) ||
            options.ParseRateLimitWindow > TimeSpan.FromDays(1))
        {
            failures.Add(
                "ParseRateLimitWindow must be between one second and one day.");
        }

        if (options.ParseRateLimitQueueLimit is < 0 or > 100)
        {
            failures.Add("ParseRateLimitQueueLimit must be between 0 and 100.");
        }

        if (options.ParseCooldown < TimeSpan.Zero ||
            options.ParseCooldown > TimeSpan.FromDays(1))
        {
            failures.Add("ParseCooldown must be between zero and one day.");
        }

        RequirePositive(options.PollInterval, nameof(options.PollInterval), failures);
        RequirePositive(options.Lifetime, nameof(options.Lifetime), failures);
        RequirePositive(options.LeaseDuration, nameof(options.LeaseDuration), failures);
        RequirePositive(options.ProviderTimeout, nameof(options.ProviderTimeout), failures);
        RequirePositive(
            options.FinalizationSafetyMargin,
            nameof(options.FinalizationSafetyMargin),
            failures);
        RequirePositive(options.RetryBaseDelay, nameof(options.RetryBaseDelay), failures);
        RequirePositive(
            options.ShutdownGracePeriod,
            nameof(options.ShutdownGracePeriod),
            failures);

        if (options.ShutdownGracePeriod > TimeSpan.FromMinutes(5))
        {
            failures.Add("ShutdownGracePeriod cannot exceed five minutes.");
        }

        if (options.MaximumAttempts < 1)
        {
            failures.Add("MaximumAttempts must be at least 1.");
        }

        if (options.RetryMaximumDelay < options.RetryBaseDelay)
        {
            failures.Add("RetryMaximumDelay must be greater than or equal to RetryBaseDelay.");
        }

        if (options.RetryJitterMaximum < TimeSpan.Zero)
        {
            failures.Add("RetryJitterMaximum cannot be negative.");
        }

        if (options.LeaseDuration <= options.ProviderTimeout ||
            options.LeaseDuration - options.ProviderTimeout <=
                options.FinalizationSafetyMargin)
        {
            failures.Add(
                "LeaseDuration must be greater than ProviderTimeout plus " +
                "FinalizationSafetyMargin.");
        }

        if (options.ProviderTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add("ProviderTimeout cannot exceed five minutes.");
        }

        if (options.LeaseDuration > TimeSpan.FromMinutes(30))
        {
            failures.Add("LeaseDuration cannot exceed thirty minutes.");
        }

        if (options.FinalizationSafetyMargin > TimeSpan.FromMinutes(5))
        {
            failures.Add("FinalizationSafetyMargin cannot exceed five minutes.");
        }

        if (options.RetentionPeriod < TimeSpan.FromDays(1) ||
            options.RetentionPeriod > TimeSpan.FromDays(3650))
        {
            failures.Add("RetentionPeriod must be between one day and ten years.");
        }

        if (options.PurgeBatchSize is < 1 or > 1_000)
        {
            failures.Add("PurgeBatchSize must be between 1 and 1000.");
        }

        if (options.PurgePollInterval < TimeSpan.FromMinutes(1) ||
            options.PurgePollInterval > TimeSpan.FromDays(1))
        {
            failures.Add("PurgePollInterval must be between one minute and one day.");
        }

        if (string.IsNullOrWhiteSpace(options.DistributedProviderScope) ||
            options.DistributedProviderScope.Length > 100)
        {
            failures.Add("DistributedProviderScope must contain between 1 and 100 characters.");
        }

        if (options.DistributedMaxConcurrentRequests is < 1 or > 100)
        {
            failures.Add("DistributedMaxConcurrentRequests must be between 1 and 100.");
        }

        if (options.ProviderCapacityRetryDelay < TimeSpan.FromSeconds(1) ||
            options.ProviderCapacityRetryDelay > TimeSpan.FromMinutes(5))
        {
            failures.Add("ProviderCapacityRetryDelay must be between one second and five minutes.");
        }

        if (options.CircuitBreakerFailureThreshold is < 1 or > 100)
        {
            failures.Add("CircuitBreakerFailureThreshold must be between 1 and 100.");
        }

        if (options.CircuitBreakerOpenDuration < TimeSpan.FromSeconds(1) ||
            options.CircuitBreakerOpenDuration > TimeSpan.FromHours(1))
        {
            failures.Add("CircuitBreakerOpenDuration must be between one second and one hour.");
        }

        if (options.CircuitBreakerHalfOpenMaxProbes is < 1 or > 10)
        {
            failures.Add("CircuitBreakerHalfOpenMaxProbes must be between 1 and 10.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(
        TimeSpan value,
        string name,
        ICollection<string> failures)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{name} must be greater than zero.");
        }
    }
}
