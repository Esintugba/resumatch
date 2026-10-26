using Api.Application.JobParsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class JobPostingParseDraftOptionsValidatorTests
{
    private readonly JobPostingParseDraftOptionsValidator _validator = new();

    [Fact]
    public void Default_options_are_valid_and_include_finalization_margin()
    {
        var options = new JobPostingParseDraftOptions();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
        Assert.True(
            options.LeaseDuration >
            options.ProviderTimeout + options.FinalizationSafetyMargin);
        Assert.False(options.PurgeEnabled);
        Assert.Equal(TimeSpan.FromDays(30), options.RetentionPeriod);
        Assert.Equal(100, options.PurgeBatchSize);
        Assert.Equal(TimeSpan.FromHours(1), options.PurgePollInterval);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3651, 1001, 86401)]
    public void Invalid_purge_options_are_rejected(
        int retentionDays,
        int batchSize,
        int pollSeconds)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new JobPostingParseDraftOptions
            {
                RetentionPeriod = TimeSpan.FromDays(retentionDays),
                PurgeBatchSize = batchSize,
                PurgePollInterval = TimeSpan.FromSeconds(pollSeconds)
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, value => value.StartsWith("RetentionPeriod"));
        Assert.Contains(result.Failures, value => value.StartsWith("PurgeBatchSize"));
        Assert.Contains(result.Failures, value => value.StartsWith("PurgePollInterval"));
    }

    [Fact]
    public void Lease_must_exceed_provider_timeout_and_finalization_margin()
    {
        var options = new JobPostingParseDraftOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(45),
            ProviderTimeout = TimeSpan.FromSeconds(30),
            FinalizationSafetyMargin = TimeSpan.FromSeconds(15)
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("FinalizationSafetyMargin", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_poll_retry_attempt_and_shutdown_values_are_rejected()
    {
        var options = new JobPostingParseDraftOptions
        {
            PollInterval = TimeSpan.Zero,
            MaximumAttempts = 0,
            RetryBaseDelay = TimeSpan.FromMinutes(2),
            RetryMaximumDelay = TimeSpan.FromMinutes(1),
            ShutdownGracePeriod = TimeSpan.Zero
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, value => value.StartsWith("PollInterval"));
        Assert.Contains(result.Failures, value => value.StartsWith("MaximumAttempts"));
        Assert.Contains(result.Failures, value => value.StartsWith("RetryMaximumDelay"));
        Assert.Contains(result.Failures, value => value.StartsWith("ShutdownGracePeriod"));
    }

    [Fact]
    public void Validate_on_start_throws_for_invalid_configuration()
    {
        var services = new ServiceCollection();
        services.AddOptions<JobPostingParseDraftOptions>()
            .Configure(options => { })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JobPostingParseDraftOptions>>(
            new AlwaysInvalidValidator());
        services.AddOptionsWithValidateOnStart<JobPostingParseDraftOptions>();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<JobPostingParseDraftOptions>>()
                .Value);

        Assert.Contains("startup validation failure", exception.Failures);
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1001, 101, 86401)]
    public void Invalid_parse_rate_limit_values_are_rejected(
        int requests,
        int queueLimit,
        int windowSeconds)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new JobPostingParseDraftOptions
            {
                ParseRequestsPerWindow = requests,
                ParseRateLimitQueueLimit = queueLimit,
                ParseRateLimitWindow = TimeSpan.FromSeconds(windowSeconds)
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ParseRequestsPerWindow",
            StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ParseRateLimitQueueLimit",
            StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ParseRateLimitWindow",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Excessive_timeout_lease_margin_and_cooldown_are_rejected()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new JobPostingParseDraftOptions
            {
                ProviderTimeout = TimeSpan.FromMinutes(6),
                LeaseDuration = TimeSpan.FromMinutes(31),
                FinalizationSafetyMargin = TimeSpan.FromMinutes(6),
                ParseCooldown = TimeSpan.FromDays(2)
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ProviderTimeout",
            StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "LeaseDuration",
            StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "FinalizationSafetyMargin",
            StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ParseCooldown",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Excessive_shutdown_grace_period_is_rejected()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new JobPostingParseDraftOptions
            {
                ShutdownGracePeriod = TimeSpan.FromMinutes(5) +
                    TimeSpan.FromMilliseconds(1)
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith(
            "ShutdownGracePeriod",
            StringComparison.Ordinal));
    }

    private sealed class AlwaysInvalidValidator :
        IValidateOptions<JobPostingParseDraftOptions>
    {
        public ValidateOptionsResult Validate(
            string? name,
            JobPostingParseDraftOptions options) =>
            ValidateOptionsResult.Fail("startup validation failure");
    }
}
