using System.Diagnostics;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Diagnostics;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing;

public sealed class JobPostingParseDraftPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobPostingParseDraftPurgeBackgroundService> _logger;
    private readonly bool _enabled;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;

    public JobPostingParseDraftPurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<JobPostingParseDraftOptions> options,
        TimeProvider timeProvider,
        ILogger<JobPostingParseDraftPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.Value.PurgeEnabled;
        _batchSize = options.Value.PurgeBatchSize;
        _pollInterval = options.Value.PurgePollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Job parse draft purge worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var purgeService = scope.ServiceProvider
                    .GetRequiredService<IJobPostingParseDraftPurgeService>();
                var purged = await purgeService.PurgeBatchAsync(stoppingToken);
                _logger.LogInformation(
                    "Job parse draft purge batch completed. PurgedDraftCount={PurgedDraftCount} " +
                    "ElapsedMilliseconds={ElapsedMilliseconds}",
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
                JobPostingParseDraftMetrics.PurgeFailures.Add(1);
                _logger.LogError(
                    "Job parse draft purge batch failed. Operation={Operation} " +
                    "ExceptionType={ExceptionType}",
                    "purge_batch",
                    exception.GetType().Name);
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
