using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Observability;

public sealed class ConfigurationHealthCheck : IHealthCheck
{
    private readonly JobPostingParseDraftOptions _draftOptions;
    private readonly OpenAiJobDescriptionExtractionOptions _providerOptions;

    public ConfigurationHealthCheck(
        IOptions<JobPostingParseDraftOptions> draftOptions,
        IOptions<OpenAiJobDescriptionExtractionOptions> providerOptions)
    {
        ArgumentNullException.ThrowIfNull(draftOptions);
        ArgumentNullException.ThrowIfNull(providerOptions);
        _draftOptions = draftOptions.Value;
        _providerOptions = providerOptions.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var invalid = _draftOptions.WorkerEnabled &&
            _draftOptions.ProviderExecutionEnabled &&
            _providerOptions.Provider ==
                JobDescriptionExtractionProviderKind.Unavailable;
        return Task.FromResult(
            invalid ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());
    }
}

public sealed class JobParserOperationalHealthCheck : IHealthCheck
{
    private readonly JobPostingParseDraftOptions _options;

    public JobParserOperationalHealthCheck(
        IOptions<JobPostingParseDraftOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = _options.WorkerEnabled &&
            !_options.ProviderExecutionEnabled
                ? HealthCheckResult.Degraded()
                : HealthCheckResult.Healthy();
        return Task.FromResult(result);
    }
}
