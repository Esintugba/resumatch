namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftOptions
{
    public const string SectionName = "JobPostingParseDrafts";

    public bool WorkerEnabled { get; init; }
    public bool ProviderExecutionEnabled { get; init; } = true;
    public bool PurgeEnabled { get; init; }
    public bool ParseRateLimitEnabled { get; init; } = true;
    public int ParseRequestsPerWindow { get; init; } = 10;
    public TimeSpan ParseRateLimitWindow { get; init; } = TimeSpan.FromMinutes(15);
    public int ParseRateLimitQueueLimit { get; init; }
    public TimeSpan ParseCooldown { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public int MaximumAttempts { get; init; } = 3;
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan FinalizationSafetyMargin { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetryMaximumDelay { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan RetryJitterMaximum { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan ShutdownGracePeriod { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(30);
    public int PurgeBatchSize { get; init; } = 100;
    public TimeSpan PurgePollInterval { get; init; } = TimeSpan.FromHours(1);
    public string DistributedProviderScope { get; init; } =
        "job-description-extraction";
    public int DistributedMaxConcurrentRequests { get; init; } = 1;
    public TimeSpan ProviderCapacityRetryDelay { get; init; } =
        TimeSpan.FromSeconds(5);
    public bool CircuitBreakerEnabled { get; init; } = true;
    public int CircuitBreakerFailureThreshold { get; init; } = 3;
    public TimeSpan CircuitBreakerOpenDuration { get; init; } =
        TimeSpan.FromMinutes(1);
    public int CircuitBreakerHalfOpenMaxProbes { get; init; } = 1;
}
