using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "AuthenticationRateLimits";

    public int LoginPermitLimit { get; init; } = 5;
    public TimeSpan LoginWindow { get; init; } = TimeSpan.FromMinutes(1);
    public int RegisterPermitLimit { get; init; } = 3;
    public TimeSpan RegisterWindow { get; init; } = TimeSpan.FromMinutes(10);
    public int QueueLimit { get; init; }
}

public sealed class AuthenticationRateLimitOptionsValidator :
    IValidateOptions<AuthenticationRateLimitOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AuthenticationRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidatePolicy(
            "Login",
            options.LoginPermitLimit,
            options.LoginWindow,
            failures);
        ValidatePolicy(
            "Register",
            options.RegisterPermitLimit,
            options.RegisterWindow,
            failures);

        if (options.QueueLimit != 0)
        {
            failures.Add("Authentication rate-limit QueueLimit must be zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePolicy(
        string policy,
        int permitLimit,
        TimeSpan window,
        ICollection<string> failures)
    {
        if (permitLimit is < 1 or > 100)
        {
            failures.Add($"{policy}PermitLimit must be between 1 and 100.");
        }

        if (window < TimeSpan.FromSeconds(1) ||
            window > TimeSpan.FromHours(1))
        {
            failures.Add(
                $"{policy}Window must be between one second and one hour.");
        }
    }
}
