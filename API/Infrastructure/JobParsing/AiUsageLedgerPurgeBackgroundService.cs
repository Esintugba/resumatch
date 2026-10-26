using System.Diagnostics;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Diagnostics;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing;

public sealed class AiUsageLedgerPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiUsageLedgerPurgeBackgroundService> _logger;
    private readonly bool _enabled;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;

    public AiUsageLedgerPurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AiUsageBudgetOptions> options,
        ILogger<AiUsageLedgerPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.Value.Enabled &&
            options.Value.Reconciliation.Purge.Enabled;
        _batchSize = options.Value.Reconciliation.Purge.BatchSize;
        _pollInterval = options.Value.Reconciliation.Purge.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("AI usage ledger purge worker is disabled.");
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
                var purged = await service.PurgeResolvedReservationsAsync(stoppingToken);
                _logger.LogInformation(
                    "AI usage ledger purge completed. Count={Count} ElapsedMilliseconds={ElapsedMilliseconds}",
                    purged,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                if (purged == _batchSize)
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
                JobPostingParseDraftMetrics.BudgetLedgerPurgeFailures.Add(1);
                _logger.LogError(
                    "AI usage ledger purge failed. Operation={Operation} ExceptionType={ExceptionType}",
                    "purge_resolved", exception.GetType().Name);
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
