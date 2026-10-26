using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Observability;

public sealed class JobParsingOperationalSampler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobParsingOperationalSampler> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _queryTimeout;

    public JobParsingOperationalSampler(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ObservabilityOptions> options,
        ILogger<JobParsingOperationalSampler> logger)
    {
        _scopeFactory = scopeFactory ??
            throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value;
        _enabled = configured.Enabled && configured.MetricsEnabled &&
            configured.OperationalSamplerEnabled;
        _interval = configured.OperationalSamplerInterval;
        _queryTimeout = configured.DatabaseCheckTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.SetCommandTimeout(_queryTimeout);
            var now = _timeProvider.GetUtcNow().ToUniversalTime();

            var pending = await dbContext.JobPostingParseDrafts
                .AsNoTracking()
                .Where(item => item.Status == JobPostingParseDraftStatus.Pending)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.LongCount(),
                    Oldest = group.Min(item => item.RequestedAt)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var reconciliation = await dbContext.AiUsageReservations
                .AsNoTracking()
                .Where(item =>
                    item.Status == AiUsageReservationStatus.Expired ||
                    item.Status == AiUsageReservationStatus.ReconciliationRequired)
                .GroupBy(item => item.ReconciliationReason)
                .Select(group => new
                {
                    Reason = group.Key,
                    Count = group.LongCount(),
                    Oldest = group.Min(item => item.ReconciliationRequiredAt)
                })
                .ToListAsync(cancellationToken);

            var reasonCounts = reconciliation
                .Where(item => item.Reason is not null)
                .ToDictionary(item => item.Reason!.Value, item => item.Count);
            var oldestReconciliation = reconciliation
                .Where(item => item.Oldest is not null)
                .Select(item => item.Oldest!.Value)
                .DefaultIfEmpty(now)
                .Min();

            JobPostingParseDraftMetrics.SetOperationalSnapshot(
                pending?.Count ?? 0,
                pending is null ? 0 : Math.Max(0, (now - pending.Oldest).TotalSeconds),
                reasonCounts,
                reconciliation.Count == 0
                    ? 0
                    : Math.Max(0, (now - oldestReconciliation).TotalSeconds));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            JobPostingParseDraftMetrics.OperationalSamplerFailures.Add(1);
            _logger.LogWarning(
                "Operational metric sampling failed. Operation={Operation} ExceptionType={ExceptionType}",
                "job_parsing_queue_sample",
                exception.GetType().Name);
        }
    }
}
