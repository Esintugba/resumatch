using System.Diagnostics;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Application.JobParsing.Diagnostics;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing;

public sealed class JobPostingParseDraftBackgroundService : BackgroundService
{
    private const string WorkerPrefix = "job-parser-";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobPostingParseDraftBackgroundService> _logger;
    private readonly bool _enabled;
    private readonly bool _providerExecutionEnabled;
    private readonly IOptionsMonitor<JobPostingParseDraftOptions>? _optionsMonitor;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _shutdownGracePeriod;

    public JobPostingParseDraftBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<JobPostingParseDraftOptions> options,
        ILogger<JobPostingParseDraftBackgroundService> logger,
        IOptionsMonitor<JobPostingParseDraftOptions>? optionsMonitor = null)
    {
        _scopeFactory = scopeFactory ??
            throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);

        _enabled = options.Value.WorkerEnabled;
        _providerExecutionEnabled = options.Value.ProviderExecutionEnabled;
        _optionsMonitor = optionsMonitor;
        _pollInterval = options.Value.PollInterval;
        _shutdownGracePeriod = options.Value.ShutdownGracePeriod;
        WorkerIdentity = CreateWorkerIdentity();
    }

    public string WorkerIdentity { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Job parse draft worker is disabled.");
            return;
        }

        _logger.LogInformation("Job parse draft worker started.");
        var disabledWasLogged = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsProviderExecutionEnabled())
            {
                if (!disabledWasLogged)
                {
                    disabledWasLogged = true;
                    JobPostingParseDraftMetrics.ProviderExecutionDisabled.Add(1);
                    _logger.LogWarning(
                        "Job parse draft provider execution is disabled by operator policy.");
                }

                if (!await DelayUntilNextPollAsync(stoppingToken))
                {
                    break;
                }

                continue;
            }

            disabledWasLogged = false;
            var foundWork = await ProcessNextAsync(stoppingToken);
            if (!foundWork)
            {
                _logger.LogDebug(
                    "Job parse draft queue is empty. " +
                    "PollIntervalMilliseconds={PollIntervalMilliseconds}",
                    _pollInterval.TotalMilliseconds);

                if (!await DelayUntilNextPollAsync(stoppingToken))
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Job parse draft worker stopped gracefully.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job parse draft worker shutdown requested.");

        using var gracePeriod = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        gracePeriod.CancelAfter(_shutdownGracePeriod);

        try
        {
            await base.StopAsync(gracePeriod.Token);
        }
        catch (OperationCanceledException)
            when (gracePeriod.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Job parse draft worker exceeded its shutdown grace period.");
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var claimService = scope.ServiceProvider
                .GetRequiredService<IJobPostingParseDraftClaimService>();
            var executionService = scope.ServiceProvider
                .GetRequiredService<IJobPostingParseDraftExecutionService>();

            var claim = await claimService.ClaimNextAsync(
                WorkerIdentity,
                stoppingToken);
            if (claim is null)
            {
                return false;
            }

            LogClaim(claim);
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await executionService.ExecuteAsync(claim, stoppingToken);
                _logger.LogInformation(
                    "Job parse draft execution cycle finished. AttemptCount={AttemptCount} " +
                    "ParserVersion={ParserVersion} ElapsedMilliseconds={ElapsedMilliseconds}",
                    claim.AttemptCount,
                    claim.ParserVersion,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogUnexpectedFailure(claim, exception);
            }

            return true;
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            JobPostingParseDraftMetrics.WorkerIterationFailures.Add(1);
            _logger.LogError(
                "Job parse draft worker iteration failed. " +
                "FailureCode={FailureCode} ExceptionType={ExceptionType}",
                JobParseFailureClassifier.UnexpectedFailureCode,
                exception.GetType().Name);
            return false;
        }
    }

    private bool IsProviderExecutionEnabled() =>
        _optionsMonitor?.CurrentValue.ProviderExecutionEnabled ??
        _providerExecutionEnabled;

    private async Task<bool> DelayUntilNextPollAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_pollInterval, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void LogClaim(JobPostingParseDraftClaim claim)
    {
        _logger.LogInformation(
            "Job parse draft claimed. AttemptCount={AttemptCount} " +
            "ParserVersion={ParserVersion}",
            claim.AttemptCount,
            claim.ParserVersion);

        if (claim.IsLeaseRecovery)
        {
            _logger.LogWarning(
                "Expired job parse draft lease recovered. AttemptCount={AttemptCount} " +
                "ParserVersion={ParserVersion}",
                claim.AttemptCount,
                claim.ParserVersion);
        }
    }

    private void LogUnexpectedFailure(
        JobPostingParseDraftClaim claim,
        Exception exception)
    {
        _logger.LogError(
            "Job parse draft execution crashed; its lease will be eligible for recovery. " +
            "AttemptCount={AttemptCount} ParserVersion={ParserVersion} " +
            "FailureCode={FailureCode} ExceptionType={ExceptionType}",
            claim.AttemptCount,
            claim.ParserVersion,
            JobParseFailureClassifier.UnexpectedFailureCode,
            exception.GetType().Name);
    }

    private static string CreateWorkerIdentity() =>
        WorkerPrefix + Guid.NewGuid().ToString("N");
}
