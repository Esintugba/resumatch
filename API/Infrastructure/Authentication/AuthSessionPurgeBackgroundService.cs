using Api.Application.Auth;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Authentication;

public sealed class AuthSessionPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthSessionPurgeBackgroundService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;

    public AuthSessionPurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AuthSessionOptions> options,
        ILogger<AuthSessionPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.Value.PurgeEnabled;
        _interval = options.Value.PurgeInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Auth session purge worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
                var purged = await service.PurgeExpiredAsync(stoppingToken);
                _logger.LogInformation("Auth session purge completed. Purged={Purged}", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Auth session purge failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
