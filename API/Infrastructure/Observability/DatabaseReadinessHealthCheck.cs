using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Api.Infrastructure.Observability;

public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _timeout;
    private readonly ILogger<DatabaseReadinessHealthCheck> _logger;

    public DatabaseReadinessHealthCheck(
        IServiceScopeFactory scopeFactory,
        IOptions<ObservabilityOptions> options,
        ILogger<DatabaseReadinessHealthCheck> logger)
    {
        _scopeFactory = scopeFactory ??
            throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _timeout = options.Value.DatabaseCheckTimeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync(timeout.Token);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.CommandTimeout = Math.Max(
                    1,
                    (int)Math.Ceiling(_timeout.TotalSeconds));
                var result = await command.ExecuteScalarAsync(timeout.Token);
                return result is 1
                    ? HealthCheckResult.Healthy()
                    : RecordFailure("unexpected_result", null);
            }
            finally
            {
                if (connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return RecordFailure("timeout", typeof(OperationCanceledException));
        }
        catch (Exception exception)
        {
            return RecordFailure("connection_error", exception.GetType());
        }
    }

    private HealthCheckResult RecordFailure(string reason, Type? exceptionType)
    {
        JobPostingParseDraftMetrics.ReadinessFailures.Add(
            1,
            new KeyValuePair<string, object?>("check", "database"),
            new KeyValuePair<string, object?>("reason", reason));
        _logger.LogWarning(
            "Readiness check failed. Check={Check} Reason={Reason} ExceptionType={ExceptionType}",
            "database",
            reason,
            exceptionType?.Name);
        return HealthCheckResult.Unhealthy();
    }
}
