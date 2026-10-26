using System.Diagnostics.Metrics;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class AiUsageBudgetServiceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    public AiUsageBudgetServiceTests(PostgreSqlFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task ReserveAsync_AtomicallyReservesUserAndSystemDailyAndMonthlyPeriods()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var result = await CreateService(context).ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);

        Assert.Equal(AiUsageReservationOutcome.Reserved, result.Outcome);
        Assert.Equal(4, await context.AiUsageBudgetPeriods.CountAsync());
        Assert.All(await context.AiUsageBudgetPeriods.ToListAsync(), period =>
        {
            Assert.Equal(1, period.ReservedRequests);
            Assert.Equal(150, period.ReservedTokens);
        });
        Assert.Equal(4, await context.AiUsageReservationPeriods.CountAsync());
    }

    [Fact]
    public async Task ReserveAsync_UserLimitExceeded_LeavesEveryScopeUnchanged()
    {
        var userId = Guid.NewGuid();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, userRequestLimit: 1, systemRequestLimit: 10);
        await service.ReserveAsync(Request(userId, Guid.NewGuid(), 1), default);
        context.ChangeTracker.Clear();

        var denied = await service.ReserveAsync(Request(userId, Guid.NewGuid(), 1), default);

        Assert.Equal(AiUsageReservationOutcome.UserBudgetExceeded, denied.Outcome);
        Assert.Equal(1, await context.AiUsageReservations.CountAsync());
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(1, period.ReservedRequests));
    }

    [Fact]
    public async Task ReserveAsync_DenialMetricUsesOnlyBoundedScopeTag()
    {
        var measurements = new List<(long Value, string[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JobPostingParseDraftMetrics.MeterName &&
                instrument.Name == "resumatch.job_parsing.budget.denied")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((
                value,
                tags.ToArray().Select(item => $"{item.Key}={item.Value}").ToArray())));
        listener.Start();
        var userId = Guid.NewGuid();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, userRequestLimit: 1);
        await service.ReserveAsync(Request(userId, Guid.NewGuid(), 1), default);
        context.ChangeTracker.Clear();

        await service.ReserveAsync(Request(userId, Guid.NewGuid(), 1), default);

        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(["scope=user"], measurement.Tags);
    }

    [Fact]
    public async Task ReserveAsync_SystemLimitExceeded_LeavesEveryScopeUnchanged()
    {
        var firstUserId = Guid.NewGuid();
        var deniedUserId = Guid.NewGuid();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, userRequestLimit: 10, systemRequestLimit: 1);
        await service.ReserveAsync(Request(firstUserId, Guid.NewGuid(), 1), default);
        context.ChangeTracker.Clear();

        var denied = await service.ReserveAsync(
            Request(deniedUserId, Guid.NewGuid(), 1), default);

        Assert.Equal(AiUsageReservationOutcome.SystemBudgetExceeded, denied.Outcome);
        Assert.Equal(1, await context.AiUsageReservations.CountAsync());
        var periods = await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync();
        Assert.All(periods.Where(period =>
                period.ScopeType == AiUsageBudgetScopeType.System ||
                period.UserId != deniedUserId),
            period => Assert.Equal(1, period.ReservedRequests));
        Assert.All(periods.Where(period => period.UserId == deniedUserId),
            period => Assert.Equal(0, period.ReservedRequests));
    }

    [Fact]
    public async Task ReserveAsync_ConcurrentLastSystemCapacity_AllowsOnlyOneReplica()
    {
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var first = CreateService(firstContext, systemRequestLimit: 1);
        var second = CreateService(secondContext, systemRequestLimit: 1);

        var results = await Task.WhenAll(
            first.ReserveAsync(Request(Guid.NewGuid(), Guid.NewGuid(), 1), default),
            second.ReserveAsync(Request(Guid.NewGuid(), Guid.NewGuid(), 1), default));

        Assert.Single(results, item => item.Outcome == AiUsageReservationOutcome.Reserved);
        Assert.Single(results, item => item.Outcome == AiUsageReservationOutcome.SystemBudgetExceeded);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await verification.AiUsageReservations.CountAsync());
    }

    [Fact]
    public async Task ReserveAsync_SameAttempt_IsIdempotent()
    {
        var request = Request(Guid.NewGuid(), Guid.NewGuid(), 1);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        var first = await service.ReserveAsync(request, default);
        context.ChangeTracker.Clear();

        var second = await service.ReserveAsync(request, default);

        Assert.Equal(AiUsageReservationOutcome.Duplicate, second.Outcome);
        Assert.Equal(first.ReservationId, second.ReservationId);
        Assert.Equal(1, await context.AiUsageReservations.CountAsync());
    }

    [Fact]
    public async Task SettleAsync_ReleasesReservedAndAddsActualUsageExactlyOnce()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);

        var first = await service.SettleAsync(new(
            reserved.ReservationId!.Value,
            new ProviderTokenUsage(40, 20, 60), "openai", "model"), default);
        var second = await service.SettleAsync(new(
            reserved.ReservationId.Value,
            new ProviderTokenUsage(40, 20, 60), "openai", "model"), default);

        Assert.True(first.StateChanged);
        Assert.False(second.StateChanged);
        context.ChangeTracker.Clear();
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(), period =>
        {
            Assert.Equal(0, period.ReservedRequests);
            Assert.Equal(0, period.ReservedTokens);
            Assert.Equal(1, period.ConsumedRequests);
            Assert.Equal(60, period.ConsumedTotalTokens);
        });
    }

    [Fact]
    public async Task ReleaseAsync_IsIdempotentAndNeverMakesCountersNegative()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);

        Assert.True((await service.ReleaseAsync(reserved.ReservationId!.Value, default)).StateChanged);
        Assert.False((await service.ReleaseAsync(reserved.ReservationId.Value, default)).StateChanged);
        context.ChangeTracker.Clear();
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(), period =>
        {
            Assert.Equal(0, period.ReservedRequests);
            Assert.Equal(0, period.ReservedTokens);
            Assert.Equal(0, period.ConsumedRequests);
        });
    }

    [Fact]
    public async Task SettleAsync_MissingUsage_RequiresReconciliation()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);
        var result = await service.SettleAsync(new(
            reserved.ReservationId!.Value, null, "openai", "model"), default);

        Assert.Equal(AiUsageReservationStatus.ReconciliationRequired, result.Status);
        context.ChangeTracker.Clear();
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(150, period.ReservedTokens));
    }

    [Fact]
    public async Task SettleAsync_GreaterThanReserved_PersistsFullUsageAndOverage()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);

        var result = await service.SettleAsync(new(
            reserved.ReservationId!.Value,
            new ProviderTokenUsage(120, 80, 200),
            "openai", "model"), default);

        Assert.Equal(AiUsageReservationStatus.Settled, result.Status);
        context.ChangeTracker.Clear();
        var entity = await context.AiUsageReservations.AsNoTracking().SingleAsync();
        Assert.Equal(200, entity.ActualTotalTokens);
        Assert.Equal(50, entity.OverageTotalTokens);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(200, period.ConsumedTotalTokens));
    }

    [Fact]
    public async Task SettleAsync_AfterPeriodBoundary_ConsumesOriginatingPeriods()
    {
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, clock: clock);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);
        clock.UtcNow = new DateTimeOffset(2026, 11, 1, 0, 1, 0, TimeSpan.Zero);

        await service.SettleAsync(new(
            reserved.ReservationId!.Value,
            new ProviderTokenUsage(40, 20, 60), "openai", "model"), default);

        context.ChangeTracker.Clear();
        Assert.Equal(4, await context.AiUsageBudgetPeriods.CountAsync());
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(60, period.ConsumedTotalTokens));
    }

    [Fact]
    public async Task ReconcileExpiredReservations_MarksExpiredAndKeepsCapacityReserved()
    {
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var options = CreateOptions();
        var service = new AiUsageBudgetService(context, Options.Create(options), clock);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);
        clock.UtcNow = Now.AddHours(1);
        var reconciler = new AiUsageReservationReconciliationService(
            context, clock, Options.Create(options));

        Assert.Equal(1, await reconciler.ReconcileExpiredReservationsAsync(default));
        context.ChangeTracker.Clear();
        var entity = await context.AiUsageReservations.AsNoTracking()
            .SingleAsync(item => item.Id == reserved.ReservationId);
        Assert.Equal(AiUsageReservationStatus.Expired, entity.Status);
        Assert.Equal(AiUsageReconciliationReason.ExpiredUnknownOutcome,
            entity.ReconciliationReason);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(150, period.ReservedTokens));
    }

    [Fact]
    public async Task DisabledBudget_DoesNotCreateLedgerRows()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var options = new AiUsageBudgetOptions
        {
            Enabled = false
        };
        var service = new AiUsageBudgetService(
            context, Options.Create(options), new MutableTimeProvider(Now));

        var result = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), 1), default);

        Assert.Equal(AiUsageReservationOutcome.Disabled, result.Outcome);
        Assert.Equal(0, await context.AiUsageReservations.CountAsync());
        Assert.Equal(0, await context.AiUsageBudgetPeriods.CountAsync());
    }

    private static AiUsageReservationRequest Request(Guid userId, Guid draftId, int attempt) =>
        new(userId, draftId, attempt);

    private static AiUsageBudgetService CreateService(
        AppDbContext context,
        long userRequestLimit = 10,
        long systemRequestLimit = 10,
        TimeProvider? clock = null) =>
        new(context, Options.Create(CreateOptions(userRequestLimit, systemRequestLimit)),
            clock ?? new MutableTimeProvider(Now));

    private static AiUsageBudgetOptions CreateOptions(
        long userRequestLimit = 10,
        long systemRequestLimit = 10) => new()
    {
        Enabled = true,
        User = new AiUsageBudgetLimitOptions
        {
            DailyRequestLimit = userRequestLimit,
            MonthlyRequestLimit = userRequestLimit,
            DailyTokenLimit = 10_000,
            MonthlyTokenLimit = 10_000
        },
        System = new AiUsageBudgetLimitOptions
        {
            DailyRequestLimit = systemRequestLimit,
            MonthlyRequestLimit = systemRequestLimit,
            DailyTokenLimit = 100_000,
            MonthlyTokenLimit = 100_000
        },
        Reservation = new AiUsageReservationOptions
        {
            EstimatedInputTokens = 100,
            MaximumOutputTokens = 50,
            Lifetime = TimeSpan.FromMinutes(30)
        },
        Reconciliation = new AiUsageReconciliationOptions
        {
            BatchSize = 10,
            PollInterval = TimeSpan.FromMinutes(5)
        }
    };

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
