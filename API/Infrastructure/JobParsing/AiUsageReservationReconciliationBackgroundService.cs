using System.Diagnostics;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Diagnostics;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing;

public sealed class AiUsageReservationReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiUsageReservationReconciliationBackgroundService> _logger;
    private readonly bool _enabled;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;

    public AiUsageReservationReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AiUsageBudgetOptions> options,
        ILogger<AiUsageReservationReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.Value.Enabled && options.Value.Reconciliation.Enabled;
        _batchSize = options.Value.Reconciliation.BatchSize;
        _pollInterval = options.Value.Reconciliation.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("AI usage reservation reconciliation worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<IAiUsageReservationReconciliationService>();
                var reconciled = await service
                    .ReconcileExpiredReservationsAsync(stoppingToken);
                _logger.LogInformation(
                    "AI usage reservation reconciliation completed. Count={Count} ElapsedMilliseconds={ElapsedMilliseconds}",
                    reconciled,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                if (reconciled == _batchSize)
                {
                    await Task.Yield();
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                JobPostingParseDraftMetrics.BudgetReconciliationFailures.Add(1);
                _logger.LogError(
                    "AI usage reservation reconciliation failed. Operation={Operation} ExceptionType={ExceptionType}",
                    "reconcile_expired", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(_pollInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
