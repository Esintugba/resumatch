using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Policies;
using Api.Application.JobParsing.Protection;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class AiProviderProtectionServiceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public AiProviderProtectionServiceTests(PostgreSqlFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ConcurrencyLimitOne_AllowsOnlyOneReplica()
    {
        var clock = new MutableTimeProvider(Now);
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var results = await Task.WhenAll(
            Concurrency(firstContext, clock, 1).TryAcquireAsync(
                Guid.NewGuid(), Now.AddMinutes(5), default),
            Concurrency(secondContext, clock, 1).TryAcquireAsync(
                Guid.NewGuid(), Now.AddMinutes(5), default));

        Assert.Single(results, item => item.Acquired);
        Assert.Single(results, item => !item.Acquired);
    }

    [Fact]
    public async Task ConcurrencyLimitN_AllowsNAndRejectsNPlusOne()
    {
        var clock = new MutableTimeProvider(Now);
        var results = new List<AiProviderConcurrencyAcquireResult>();
        for (var index = 0; index < 3; index++)
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            results.Add(await Concurrency(context, clock, 2).TryAcquireAsync(
                Guid.NewGuid(), Now.AddMinutes(5), default));
        }

        Assert.Equal(2, results.Count(item => item.Acquired));
        Assert.Single(results, item => !item.Acquired);
    }

    [Fact]
    public async Task ReleasedSlot_CanBeAcquiredAgain()
    {
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Concurrency(context, clock, 1);
        var first = await service.TryAcquireAsync(
            Guid.NewGuid(), Now.AddMinutes(5), default);
        Assert.True(await service.ReleaseAsync(first.Token!, default));

        var second = await service.TryAcquireAsync(
            Guid.NewGuid(), Now.AddMinutes(5), default);
        Assert.True(second.Acquired);
    }

    [Fact]
    public async Task ExpiredLease_IsReclaimedButActiveLeaseIsNot()
    {
        var clock = new MutableTimeProvider(Now);
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        var first = await Concurrency(firstContext, clock, 1).TryAcquireAsync(
            Guid.NewGuid(), Now.AddMinutes(1), default);
        await using var activeContext = Fixture.DbContextFactory.CreateDbContext();
        Assert.False((await Concurrency(activeContext, clock, 1).TryAcquireAsync(
            Guid.NewGuid(), Now.AddMinutes(5), default)).Acquired);

        clock.UtcNow = Now.AddMinutes(2);
        await using var reclaimedContext = Fixture.DbContextFactory.CreateDbContext();
        var reclaimed = await Concurrency(reclaimedContext, clock, 1).TryAcquireAsync(
            Guid.NewGuid(), clock.UtcNow.AddMinutes(5), default);
        Assert.True(reclaimed.Acquired);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(AiProviderConcurrencyLeaseStatus.Expired,
            (await verification.AiProviderConcurrencyLeases.AsNoTracking()
                .SingleAsync(item => item.Id == first.Token!.LeaseId)).Status);
    }

    [Fact]
    public async Task StaleLeaseToken_CannotReleaseReplacementLease()
    {
        var clock = new MutableTimeProvider(Now);
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        var firstService = Concurrency(firstContext, clock, 1);
        var old = await firstService.TryAcquireAsync(
            Guid.NewGuid(), Now.AddMinutes(1), default);
        clock.UtcNow = Now.AddMinutes(2);
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var current = await Concurrency(secondContext, clock, 1).TryAcquireAsync(
            Guid.NewGuid(), clock.UtcNow.AddMinutes(5), default);

        Assert.False(await firstService.ReleaseAsync(old.Token!, default));
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(AiProviderConcurrencyLeaseStatus.Active,
            (await verification.AiProviderConcurrencyLeases.AsNoTracking()
                .SingleAsync(item => item.Id == current.Token!.LeaseId)).Status);
    }

    [Fact]
    public async Task CancelledAcquisition_DoesNotLeakSlot()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Concurrency(context, new MutableTimeProvider(Now), 1).TryAcquireAsync(
                Guid.NewGuid(), Now.AddMinutes(5), cancellation.Token));
        Assert.False(await context.AiProviderConcurrencyLeases.AnyAsync());
    }

    [Fact]
    public async Task ConcurrentQualifyingFailures_OpenCircuitWithoutLostUpdates()
    {
        var clock = new MutableTimeProvider(Now);
        var tokens = new List<(AiProviderCircuitBreaker Service, AiProviderCircuitPermitToken Token)>();
        var contexts = new List<global::Api.Data.AppDbContext>();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                var context = Fixture.DbContextFactory.CreateDbContext();
                contexts.Add(context);
                var service = Circuit(context, clock, threshold: 3);
                var permit = await service.TryAcquirePermissionAsync(default);
                tokens.Add((service, permit.Token!));
            }

            await Task.WhenAll(tokens.Select(item => item.Service.RecordFailureAsync(
                item.Token, TimeoutFailure(), default)));
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var state = await verification.AiProviderCircuitStates.AsNoTracking().SingleAsync();
        Assert.Equal(AiProviderCircuitStatus.Open, state.Status);
        Assert.Equal(3, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task OpenCircuit_RejectsUntilOpenDurationEnds()
    {
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var permit = await circuit.TryAcquirePermissionAsync(default);
        await circuit.RecordFailureAsync(permit.Token!, TimeoutFailure(), default);

        var rejected = await circuit.TryAcquirePermissionAsync(default);
        Assert.False(rejected.Allowed);
        Assert.Equal(Now.AddMinutes(1), rejected.RetryAt);
    }

    [Fact]
    public async Task QualifyingFailure_EmitsCircuitOpenedMetric()
    {
        var values = new ConcurrentQueue<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JobPostingParseDraftMetrics.MeterName &&
                instrument.Name == "resumatch.job_parsing.provider.circuit.opened")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
            values.Enqueue(value));
        listener.Start();
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var permit = await circuit.TryAcquirePermissionAsync(default);

        await circuit.RecordFailureAsync(
            permit.Token!,
            new JobParseFailureClassification(
                JobParseFailureClassifier.ProviderUnavailableCode,
                true),
            default);

        Assert.Equal([1], values);
    }

    [Fact]
    public async Task HalfOpen_AllowsOnlyConfiguredProbeCount()
    {
        var clock = new MutableTimeProvider(Now);
        await OpenCircuitAsync(clock);
        clock.UtcNow = Now.AddMinutes(2);
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var results = await Task.WhenAll(
            Circuit(firstContext, clock, threshold: 1).TryAcquirePermissionAsync(default),
            Circuit(secondContext, clock, threshold: 1).TryAcquirePermissionAsync(default));

        Assert.Single(results, item => item.Allowed && item.Token!.IsHalfOpenProbe);
        Assert.Single(results, item => !item.Allowed);
    }

    [Fact]
    public async Task HalfOpenSuccess_ClosesCircuit()
    {
        var clock = new MutableTimeProvider(Now);
        await OpenCircuitAsync(clock);
        clock.UtcNow = Now.AddMinutes(2);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var probe = await circuit.TryAcquirePermissionAsync(default);

        Assert.True(await circuit.RecordSuccessAsync(probe.Token!, default));
        context.ChangeTracker.Clear();
        var state = await context.AiProviderCircuitStates.AsNoTracking().SingleAsync();
        Assert.Equal(AiProviderCircuitStatus.Closed, state.Status);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task HalfOpenFailure_ReopensAndStaleProbeCannotCloseNewGeneration()
    {
        var clock = new MutableTimeProvider(Now);
        await OpenCircuitAsync(clock);
        clock.UtcNow = Now.AddMinutes(2);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var probe = await circuit.TryAcquirePermissionAsync(default);

        Assert.True(await circuit.RecordFailureAsync(
            probe.Token!, TimeoutFailure(), default));
        Assert.False(await circuit.RecordSuccessAsync(probe.Token!, default));
        context.ChangeTracker.Clear();
        Assert.Equal(AiProviderCircuitStatus.Open,
            (await context.AiProviderCircuitStates.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ExpiredHalfOpenProbe_IsReclaimedAndStaleCompletionIsFenced()
    {
        var clock = new MutableTimeProvider(Now);
        await OpenCircuitAsync(clock);
        clock.UtcNow = Now.AddMinutes(2);
        await using var crashedContext = Fixture.DbContextFactory.CreateDbContext();
        var crashedCircuit = Circuit(crashedContext, clock, threshold: 1);
        var crashedProbe = await crashedCircuit.TryAcquirePermissionAsync(default);
        Assert.True(crashedProbe.Allowed);

        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        await using var recoveryContext = Fixture.DbContextFactory.CreateDbContext();
        var replacement = await Circuit(recoveryContext, clock, threshold: 1)
            .TryAcquirePermissionAsync(default);

        Assert.True(replacement.Allowed);
        Assert.True(replacement.Token!.IsHalfOpenProbe);
        Assert.NotEqual(crashedProbe.Token!.ProbeLeaseId,
            replacement.Token.ProbeLeaseId);
        Assert.False(await crashedCircuit.RecordSuccessAsync(
            crashedProbe.Token, default));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var leases = await verification.AiProviderCircuitProbeLeases
            .AsNoTracking()
            .OrderBy(item => item.AcquiredAt)
            .ToListAsync();
        Assert.Equal(2, leases.Count);
        Assert.Equal(AiProviderCircuitProbeStatus.Expired, leases[0].Status);
        Assert.Equal(AiProviderCircuitProbeStatus.Active, leases[1].Status);
    }

    [Fact]
    public async Task NonQualifyingFailure_DoesNotIncrementCircuitCounter()
    {
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var permit = await circuit.TryAcquirePermissionAsync(default);
        await circuit.RecordFailureAsync(permit.Token!, new(
            JobParseFailureClassifier.InvalidProviderResponseCode, false), default);

        context.ChangeTracker.Clear();
        var state = await context.AiProviderCircuitStates.AsNoTracking().SingleAsync();
        Assert.Equal(AiProviderCircuitStatus.Closed, state.Status);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    private async Task OpenCircuitAsync(MutableTimeProvider clock)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = Circuit(context, clock, threshold: 1);
        var permit = await circuit.TryAcquirePermissionAsync(default);
        await circuit.RecordFailureAsync(permit.Token!, TimeoutFailure(), default);
    }

    private static AiProviderConcurrencyService Concurrency(
        global::Api.Data.AppDbContext context,
        TimeProvider clock,
        int limit) => new(context, clock, Options.Create(OptionsFor(limit)));

    private static AiProviderCircuitBreaker Circuit(
        global::Api.Data.AppDbContext context,
        TimeProvider clock,
        int threshold) => new(context, clock, Options.Create(OptionsFor(
            distributedLimit: 3, threshold: threshold)));

    private static JobPostingParseDraftOptions OptionsFor(
        int distributedLimit,
        int threshold = 3) => new()
    {
        DistributedProviderScope = "test-provider",
        DistributedMaxConcurrentRequests = distributedLimit,
        CircuitBreakerEnabled = true,
        CircuitBreakerFailureThreshold = threshold,
        CircuitBreakerOpenDuration = TimeSpan.FromMinutes(1),
        CircuitBreakerHalfOpenMaxProbes = 1,
        ProviderCapacityRetryDelay = TimeSpan.FromSeconds(5)
    };

    private static JobParseFailureClassification TimeoutFailure() =>
        new(JobParseFailureClassifier.ProviderTimeoutCode, true);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
