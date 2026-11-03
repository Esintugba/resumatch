using Microsoft.Extensions.Options;

namespace Api.Application.Auth;

public sealed class AuthSessionOptions
{
    public const string SectionName = "AuthSessions";

    public TimeSpan RefreshTokenIdleLifetime { get; init; } = TimeSpan.FromDays(14);
    public TimeSpan RefreshTokenAbsoluteLifetime { get; init; } = TimeSpan.FromDays(30);
    public int MaximumActiveSessionsPerUser { get; init; } = 10;

    // Two tabs refreshing with the same token: the slower one gets a soft
    // rejection inside this window instead of revoking the session as reuse.
    public TimeSpan RotationGracePeriod { get; init; } = TimeSpan.FromSeconds(10);

    public bool PurgeEnabled { get; init; } = true;
    public TimeSpan PurgeRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan PurgeInterval { get; init; } = TimeSpan.FromDays(1);
    public int PurgeBatchSize { get; init; } = 500;
}

public sealed class AuthSessionOptionsValidator : IValidateOptions<AuthSessionOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.RefreshTokenIdleLifetime < TimeSpan.FromHours(1) ||
            options.RefreshTokenIdleLifetime > TimeSpan.FromDays(90))
        {
            failures.Add("RefreshTokenIdleLifetime must be between 1 hour and 90 days.");
        }

        if (options.RefreshTokenAbsoluteLifetime < options.RefreshTokenIdleLifetime ||
            options.RefreshTokenAbsoluteLifetime > TimeSpan.FromDays(365))
        {
            failures.Add(
                "RefreshTokenAbsoluteLifetime must be at least RefreshTokenIdleLifetime and at most 365 days.");
        }

        if (options.MaximumActiveSessionsPerUser is < 1 or > 100)
        {
            failures.Add("MaximumActiveSessionsPerUser must be between 1 and 100.");
        }

        if (options.RotationGracePeriod < TimeSpan.Zero ||
            options.RotationGracePeriod > TimeSpan.FromMinutes(1))
        {
            failures.Add("RotationGracePeriod must be between 0 and 1 minute.");
        }

        if (options.PurgeRetention < TimeSpan.FromDays(1) ||
            options.PurgeRetention > TimeSpan.FromDays(90))
        {
            failures.Add("PurgeRetention must be between 1 and 90 days.");
        }

        if (options.PurgeInterval < TimeSpan.FromMinutes(1) ||
            options.PurgeInterval > TimeSpan.FromDays(7))
        {
            failures.Add("PurgeInterval must be between 1 minute and 7 days.");
        }

        if (options.PurgeBatchSize is < 1 or > 10_000)
        {
            failures.Add("PurgeBatchSize must be between 1 and 10000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
