using System.Collections.Concurrent;
using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class JobPostingParseDraftPurgeBackgroundServiceTests
{
    [Fact]
    public async Task Disabled_worker_does_not_mutate_database()
    {
        var service = new StubPurgeService([]);
        await using var harness = CreateHarness(service, enabled: false);

        await harness.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await harness.StopAsync();

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Empty_queue_waits_instead_of_busy_looping()
    {
        var service = new StubPurgeService([0]);
        await using var harness = CreateHarness(service, enabled: true);

        await harness.Worker.StartAsync(CancellationToken.None);
        await service.FirstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        await harness.StopAsync();

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Full_batch_drains_backlog_without_poll_delay()
    {
        var service = new StubPurgeService([2, 1]);
        await using var harness = CreateHarness(service, enabled: true, batchSize: 2);

        await harness.Worker.StartAsync(CancellationToken.None);
        await service.SecondCall.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task Cancellation_reaches_active_purge_operation()
    {
        var service = new StubPurgeService([], blockUntilCancellation: true);
        await using var harness = CreateHarness(service, enabled: true);

        await harness.Worker.StartAsync(CancellationToken.None);
        await service.FirstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        await service.Cancelled.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Batch_failure_is_contained_and_next_iteration_runs()
    {
        var service = new StubPurgeService([0], failFirst: true);
        await using var harness = CreateHarness(
            service,
            enabled: true,
            pollInterval: TimeSpan.FromMilliseconds(10));

        await harness.Worker.StartAsync(CancellationToken.None);
        await service.SecondCall.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        Assert.True(service.Calls >= 2);
    }

    private static Harness CreateHarness(
        StubPurgeService service,
        bool enabled,
        int batchSize = 100,
        TimeSpan? pollInterval = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(service);
        services.AddScoped<IJobPostingParseDraftPurgeService>(provider =>
            provider.GetRequiredService<StubPurgeService>());
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<JobPostingParseDraftOptions>>(
            Options.Create(new JobPostingParseDraftOptions
            {
                PurgeEnabled = enabled,
                PurgeBatchSize = batchSize,
                PurgePollInterval = pollInterval ?? TimeSpan.FromMinutes(1)
            }));
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        var worker = new JobPostingParseDraftPurgeBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<JobPostingParseDraftOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<JobPostingParseDraftPurgeBackgroundService>>());
        return new Harness(provider, worker);
    }

    private sealed class Harness(
        ServiceProvider provider,
        JobPostingParseDraftPurgeBackgroundService worker) : IAsyncDisposable
    {
        public JobPostingParseDraftPurgeBackgroundService Worker { get; } = worker;

        public async Task StopAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Worker.StopAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            Worker.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class StubPurgeService(
        IEnumerable<int> results,
        bool blockUntilCancellation = false,
        bool failFirst = false) : IJobPostingParseDraftPurgeService
    {
        private readonly ConcurrentQueue<int> _results = new(results);
        private readonly TaskCompletionSource _firstCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public Task FirstCall => _firstCall.Task;
        public Task SecondCall => _secondCall.Task;
        public Task Cancelled => _cancelled.Task;

        public async Task<int> PurgeBatchAsync(CancellationToken cancellationToken)
        {
            var calls = Interlocked.Increment(ref _calls);
            _firstCall.TrySetResult();
            if (calls >= 2)
            {
                _secondCall.TrySetResult();
            }

            if (failFirst && calls == 1)
            {
                throw new InvalidOperationException("controlled purge failure");
            }

            if (blockUntilCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _cancelled.TrySetResult();
                    throw;
                }
            }

            return _results.TryDequeue(out var result) ? result : 0;
        }
    }
}
