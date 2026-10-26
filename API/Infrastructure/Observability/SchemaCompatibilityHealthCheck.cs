using Api.Application.JobParsing.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Infrastructure.Observability;

public sealed class SchemaCompatibilityHealthCheck : IHealthCheck
{
    private readonly ISchemaCompatibilityChecker _checker;
    private readonly ILogger<SchemaCompatibilityHealthCheck> _logger;

    public SchemaCompatibilityHealthCheck(
        ISchemaCompatibilityChecker checker,
        ILogger<SchemaCompatibilityHealthCheck> logger)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _checker.CheckAsync(cancellationToken);
        if (result.IsCompatible)
        {
            return HealthCheckResult.Healthy();
        }

        var reason = result.Status.ToString();
        JobPostingParseDraftMetrics.ReadinessFailures.Add(
            1,
            new KeyValuePair<string, object?>("check", "schema"),
            new KeyValuePair<string, object?>("reason", reason));
        _logger.LogWarning(
            "Readiness check failed. Check={Check} Reason={Reason} ExceptionType={ExceptionType}",
            "schema",
            reason,
            result.ExceptionType);
        return HealthCheckResult.Unhealthy();
    }
}
