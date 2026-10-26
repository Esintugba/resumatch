using System.Collections.Concurrent;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Infrastructure.JobParsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class JobPostingParseDraftBackgroundServiceTests
{
    [Fact]
    public async Task Empty_queue_waits_for_poll_interval_instead_of_busy_looping()
    {
        var state = new WorkerState();
        await using var harness = CreateHarness(
            state,
            pollInterval: TimeSpan.FromSeconds(5));

        await harness.Worker.StartAsync(CancellationToken.None);
        await state.FirstClaim.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.Equal(1, state.ClaimCalls);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Claimed_drafts_are_executed_sequentially()
    {
        var first = CreateClaim(1);
        var second = CreateClaim(2);
        var state = new WorkerState([first, second], expectedExecutions: 2);
        await using var harness = CreateHarness(state);

        await harness.Worker.StartAsync(CancellationToken.None);
        await state.ExpectedExecutions.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        Assert.Equal([first.DraftId, second.DraftId], state.ExecutedDraftIds);
        Assert.Equal(1, state.MaximumConcurrentExecutions);
    }

    [Fact]
    public async Task One_execution_exception_does_not_block_the_next_draft()
    {
        const string sensitivePayload = "{\"secret\":\"raw-provider-json\"}";
        var first = CreateClaim(1);
        var second = CreateClaim(2);
        var state = new WorkerState([first, second], expectedExecutions: 2)
        {
            ThrowForDraftId = first.DraftId,
            ExceptionMessage = sensitivePayload
        };
        var logs = new CapturingLoggerProvider();
        await using var harness = CreateHarness(state, loggerProvider: logs);

        await harness.Worker.StartAsync(CancellationToken.None);
        await state.ExpectedExecutions.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        Assert.Equal([first.DraftId, second.DraftId], state.ExecutedDraftIds);
        Assert.DoesNotContain(
            logs.Messages,
            message => message.Contains(sensitivePayload, StringComparison.Ordinal));
        Assert.Contains(
            logs.Messages,
            message => message.Contains(
                JobParseFailureClassifier.UnexpectedFailureCode,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_cancels_active_execution_and_stops_new_claims()
    {
        var state = new WorkerState([CreateClaim(1), CreateClaim(2)])
        {
            BlockExecutionUntilCancellation = true
        };
        await using var harness = CreateHarness(state);

        await harness.Worker.StartAsync(CancellationToken.None);
        await state.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        await state.ExecutionCancelled.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, state.ClaimCalls);
        Assert.Single(state.ExecutedDraftIds);
        Assert.Equal(0, state.FailureMutationCalls);
    }

    [Fact]
    public async Task Shutdown_grace_period_bounds_an_uncooperative_execution()
    {
        var state = new WorkerState([CreateClaim(1)])
        {
            IgnoreCancellationUntilReleased = true
        };
        await using var harness = CreateHarness(
            state,
            shutdownGracePeriod: TimeSpan.FromMilliseconds(50));

        await harness.Worker.StartAsync(CancellationToken.None);
        await state.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = harness.Worker.StopAsync(CancellationToken.None);

        await stop.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(state.ExecutionFinished.IsCompleted);
        state.ReleaseExecution();
        await state.ExecutionFinished.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Disabled_worker_never_claims_work()
    {
        var state = new WorkerState([CreateClaim(1)]);
        await using var harness = CreateHarness(state, enabled: false);

        await harness.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await harness.StopAsync();

        Assert.Equal(0, state.ClaimCalls);
        Assert.Empty(state.ExecutedDraftIds);
    }

    [Fact]
    public async Task Provider_kill_switch_prevents_claims_and_busy_loop()
    {
        var state = new WorkerState([CreateClaim(1)]);
        await using var harness = CreateHarness(
            state,
            providerExecutionEnabled: false);

        await harness.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await harness.StopAsync();

        Assert.Equal(0, state.ClaimCalls);
        Assert.Empty(state.ExecutedDraftIds);
    }

    [Fact]
    public async Task Provider_kill_switch_reload_resumes_claiming_without_restart()
    {
        var state = new WorkerState([CreateClaim(1)]);
        await using var harness = CreateHarness(
            state,
            providerExecutionEnabled: false,
            pollInterval: TimeSpan.FromMilliseconds(20));

        await harness.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.Equal(0, state.ClaimCalls);

        harness.Options.Set(new JobPostingParseDraftOptions
        {
            WorkerEnabled = true,
            ProviderExecutionEnabled = true,
            PollInterval = TimeSpan.FromMilliseconds(20),
            ShutdownGracePeriod = TimeSpan.FromSeconds(1)
        });
        await state.ExpectedExecutions.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.StopAsync();

        Assert.Single(state.ExecutedDraftIds);
    }

    [Fact]
    public async Task Worker_identity_is_nonblank_bounded_and_unique_per_instance()
    {
        var first = CreateHarness(new WorkerState());
        var second = CreateHarness(new WorkerState());
        try
        {
            Assert.StartsWith("job-parser-", first.Worker.WorkerIdentity);
            Assert.InRange(first.Worker.WorkerIdentity.Length, 1, 200);
            Assert.NotEqual(
                first.Worker.WorkerIdentity,
                second.Worker.WorkerIdentity);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private static WorkerHarness CreateHarness(
        WorkerState state,
        bool enabled = true,
        bool providerExecutionEnabled = true,
        TimeSpan? pollInterval = null,
        CapturingLoggerProvider? loggerProvider = null,
        TimeSpan? shutdownGracePeriod = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<IJobPostingParseDraftClaimService>(
            provider => provider.GetRequiredService<WorkerState>());
        services.AddScoped<IJobPostingParseDraftExecutionService>(
            provider => provider.GetRequiredService<WorkerState>());
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (loggerProvider is not null)
            {
                builder.AddProvider(loggerProvider);
            }
        });
        var configured = new JobPostingParseDraftOptions
        {
            WorkerEnabled = enabled,
            ProviderExecutionEnabled = providerExecutionEnabled,
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(10),
            ShutdownGracePeriod = shutdownGracePeriod ?? TimeSpan.FromSeconds(1)
        };
        var monitor = new MutableOptionsMonitor(configured);
        services.AddSingleton<IOptions<JobPostingParseDraftOptions>>(
            Options.Create(configured));
        services.AddSingleton<IOptionsMonitor<JobPostingParseDraftOptions>>(monitor);

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        var worker = new JobPostingParseDraftBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<JobPostingParseDraftOptions>>(),
            provider.GetRequiredService<
                ILogger<JobPostingParseDraftBackgroundService>>(),
            monitor);
        return new WorkerHarness(provider, worker, monitor);
    }

    private static JobPostingParseDraftClaim CreateClaim(int sequence)
    {
        var claimedAt = new DateTimeOffset(2026, 1, 1, 0, 0, sequence, TimeSpan.Zero);
        return new JobPostingParseDraftClaim(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-worker",
            sequence,
            (uint)sequence,
            claimedAt,
            claimedAt.AddMinutes(5),
            "job-parser-v1");
    }

    private sealed class WorkerHarness(
        ServiceProvider provider,
        JobPostingParseDraftBackgroundService worker,
        MutableOptionsMonitor options) : IAsyncDisposable
    {
        public JobPostingParseDraftBackgroundService Worker { get; } = worker;
        public MutableOptionsMonitor Options { get; } = options;

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

    private sealed class MutableOptionsMonitor(
        JobPostingParseDraftOptions current) :
        IOptionsMonitor<JobPostingParseDraftOptions>
    {
        private JobPostingParseDraftOptions _current = current;

        public JobPostingParseDraftOptions CurrentValue =>
            Volatile.Read(ref _current);

        public JobPostingParseDraftOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(
            Action<JobPostingParseDraftOptions, string?> listener) => null;

        public void Set(JobPostingParseDraftOptions value) =>
            Volatile.Write(ref _current, value);
    }

    private sealed class WorkerState :
        IJobPostingParseDraftClaimService,
        IJobPostingParseDraftExecutionService
    {
        private readonly ConcurrentQueue<JobPostingParseDraftClaim> _claims;
        private readonly TaskCompletionSource _firstClaim =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionCancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionFinished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _expectedExecutions =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expectedExecutionCount;
        private int _claimCalls;
        private int _activeExecutions;
        private int _maximumConcurrentExecutions;

        public WorkerState(
            IEnumerable<JobPostingParseDraftClaim>? claims = null,
            int expectedExecutions = 1)
        {
            _claims = new ConcurrentQueue<JobPostingParseDraftClaim>(claims ?? []);
            _expectedExecutionCount = expectedExecutions;
        }

        public Task FirstClaim => _firstClaim.Task;
        public Task ExecutionStarted => _executionStarted.Task;
        public Task ExecutionCancelled => _executionCancelled.Task;
        public Task ExecutionFinished => _executionFinished.Task;
        public Task ExpectedExecutions => _expectedExecutions.Task;
        public int ClaimCalls => Volatile.Read(ref _claimCalls);
        public int MaximumConcurrentExecutions =>
            Volatile.Read(ref _maximumConcurrentExecutions);
        public int FailureMutationCalls { get; private set; }
        public List<Guid> ExecutedDraftIds { get; } = [];
        public Guid? ThrowForDraftId { get; init; }
        public string ExceptionMessage { get; init; } = "execution failed";
        public bool BlockExecutionUntilCancellation { get; init; }
        public bool IgnoreCancellationUntilReleased { get; init; }

        public void ReleaseExecution() => _executionRelease.TrySetResult();

        public Task<JobPostingParseDraftClaim?> ClaimNextAsync(
            string leaseOwner,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _claimCalls);
            _firstClaim.TrySetResult();
            return Task.FromResult(
                _claims.TryDequeue(out var claim) ? claim : null);
        }

        public async Task ExecuteAsync(
            JobPostingParseDraftClaim claim,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeExecutions);
            UpdateMaximum(active);
            lock (ExecutedDraftIds)
            {
                ExecutedDraftIds.Add(claim.DraftId);
                if (ExecutedDraftIds.Count >= _expectedExecutionCount)
                {
                    _expectedExecutions.TrySetResult();
                }
            }

            _executionStarted.TrySetResult();
            try
            {
                if (BlockExecutionUntilCancellation)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        _executionCancelled.TrySetResult();
                        throw;
                    }
                }

                if (IgnoreCancellationUntilReleased)
                {
                    await _executionRelease.Task;
                }

                if (ThrowForDraftId == claim.DraftId)
                {
                    throw new InvalidOperationException(ExceptionMessage);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeExecutions);
                _executionFinished.TrySetResult();
            }
        }

        public Task<JobPostingParseDraftClaim> RenewLeaseAsync(
            JobPostingParseDraftClaim claim,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReleaseForRetryAsync(
            JobPostingParseDraftClaim claim,
            JobParseFailureClassification failure,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken)
        {
            FailureMutationCalls++;
            throw new NotSupportedException();
        }

        public Task MarkFailedAsync(
            JobPostingParseDraftClaim claim,
            JobParseFailureClassification failure,
            CancellationToken cancellationToken)
        {
            FailureMutationCalls++;
            throw new NotSupportedException();
        }

        private void UpdateMaximum(int active)
        {
            int current;
            do
            {
                current = _maximumConcurrentExecutions;
                if (active <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                ref _maximumConcurrentExecutions,
                active,
                current) != current);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
