using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace API.Tests.Integration.JobParsing;

public sealed class AiUsageReconciliationLifecycleTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 31, 23, 50, 0, TimeSpan.Zero);

    public AiUsageReconciliationLifecycleTests(PostgreSqlFixture fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(40, 20, 60, 0)]
    [InlineData(100, 50, 150, 0)]
    [InlineData(120, 80, 200, 50)]
    public async Task ReconcileAsync_AccountsActualUsageAndOverageExactlyOnce(
        long input, long output, long total, long expectedOverage)
    {
        var seeded = await SeedUnresolvedAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, seeded.Clock, seeded.Options);
        var request = new AiUsageReconciliationRequest(
            seeded.ReservationId,
            seeded.Version,
            new ProviderTokenUsage(input, output, total));

        var first = await service.ReconcileAsync(request, default);
        var second = await service.ReconcileAsync(request, default);

        Assert.True(first.StateChanged);
        Assert.False(second.StateChanged);
        context.ChangeTracker.Clear();
        var reservation = await context.AiUsageReservations.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.ReservationId);
        Assert.Equal(AiUsageReservationStatus.Settled, reservation.Status);
        Assert.Equal(total, reservation.ActualTotalTokens);
        Assert.Equal(expectedOverage, reservation.OverageTotalTokens);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period =>
            {
                Assert.Equal(0, period.ReservedTokens);
                Assert.Equal(total, period.ConsumedTotalTokens);
            });
    }

    [Fact]
    public async Task ReconcileAsync_AfterDayBoundary_UsesOriginatingDailyPeriods()
    {
        var seeded = await SeedUnresolvedAsync();
        seeded.Clock.UtcNow = Now.AddMinutes(20);
        await ReconcileAsync(seeded, new(40, 20, 60));

        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var daily = await context.AiUsageBudgetPeriods.AsNoTracking()
            .Where(item => item.PeriodType == AiUsageBudgetPeriodType.Daily)
            .ToListAsync();
        Assert.Equal(2, daily.Count);
        Assert.All(daily, item =>
        {
            Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
                item.PeriodStart);
            Assert.Equal(60, item.ConsumedTotalTokens);
        });
    }

    [Fact]
    public async Task ReconcileAsync_AfterMonthBoundary_UsesOriginatingMonthlyPeriods()
    {
        var seeded = await SeedUnresolvedAsync();
        seeded.Clock.UtcNow = new(2026, 11, 1, 0, 10, 0, TimeSpan.Zero);
        await ReconcileAsync(seeded, new(40, 20, 60));

        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var monthly = await context.AiUsageBudgetPeriods.AsNoTracking()
            .Where(item => item.PeriodType == AiUsageBudgetPeriodType.Monthly)
            .ToListAsync();
        Assert.Equal(2, monthly.Count);
        Assert.All(monthly, item =>
        {
            Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                item.PeriodStart);
            Assert.Equal(60, item.ConsumedTotalTokens);
        });
    }

    [Fact]
    public async Task ConcurrentReconciliation_ChangesCountersOnlyOnce()
    {
        var seeded = await SeedUnresolvedAsync();
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var request = new AiUsageReconciliationRequest(
            seeded.ReservationId, seeded.Version, new(40, 20, 60));

        var results = await Task.WhenAll(
            Budget(firstContext, seeded.Clock, seeded.Options)
                .ReconcileAsync(request, default),
            Budget(secondContext, seeded.Clock, seeded.Options)
                .ReconcileAsync(request, default));

        Assert.Single(results, item => item.StateChanged);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(60, item.ConsumedTotalTokens));
    }

    [Fact]
    public async Task SettlementAndManualReconciliationRace_AccountsOnlyOnce()
    {
        var seeded = await SeedUnresolvedAsync();
        await using var settleContext = Fixture.DbContextFactory.CreateDbContext();
        await using var reconcileContext = Fixture.DbContextFactory.CreateDbContext();

        var results = await Task.WhenAll(
            Budget(settleContext, seeded.Clock, seeded.Options).SettleAsync(new(
                seeded.ReservationId,
                new ProviderTokenUsage(40, 20, 60), null, null), default),
            Budget(reconcileContext, seeded.Clock, seeded.Options).ReconcileAsync(new(
                seeded.ReservationId,
                seeded.Version,
                new ProviderTokenUsage(40, 20, 60)), default));

        Assert.Single(results, item => item.StateChanged);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(60, item.ConsumedTotalTokens));
    }

    [Fact]
    public async Task ReleaseAndUnknownOutcomeRace_ProducesOneValidLifecycle()
    {
        var clock = new MutableTimeProvider(Now);
        var options = Options();
        Guid id;
        await using (var seed = Fixture.DbContextFactory.CreateDbContext())
        {
            id = (await Budget(seed, clock, options).ReserveAsync(
                Request(Guid.NewGuid(), Guid.NewGuid()), default)).ReservationId!.Value;
        }
        await using var releaseContext = Fixture.DbContextFactory.CreateDbContext();
        await using var unknownContext = Fixture.DbContextFactory.CreateDbContext();

        var results = await Task.WhenAll(
            Budget(releaseContext, clock, options).ReleaseAsync(id, default),
            Budget(unknownContext, clock, options)
                .MarkReconciliationRequiredAsync(id, default));

        Assert.Single(results, item => item.StateChanged);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var reservation = await verification.AiUsageReservations.AsNoTracking()
            .SingleAsync();
        Assert.Contains(reservation.Status,
            new[]
            {
                AiUsageReservationStatus.Released,
                AiUsageReservationStatus.ReconciliationRequired
            });
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item =>
            {
                Assert.True(item.ReservedTokens is 0 or 150);
                Assert.Equal(0, item.ConsumedTotalTokens);
            });
    }

    [Fact]
    public async Task ConcurrentReconciliationsOnSharedPeriods_DoNotDeadlock()
    {
        var options = Options();
        var clock = new MutableTimeProvider(Now);
        var userId = Guid.NewGuid();
        var seeded = new List<(Guid Id, uint Version)>();
        for (var index = 0; index < 2; index++)
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            var service = Budget(context, clock, options);
            var reservation = await service.ReserveAsync(
                Request(userId, Guid.NewGuid()), default);
            await service.MarkReconciliationRequiredAsync(
                reservation.ReservationId!.Value, default);
            context.ChangeTracker.Clear();
            var entity = await context.AiUsageReservations.AsNoTracking()
                .SingleAsync(item => item.Id == reservation.ReservationId);
            seeded.Add((entity.Id, entity.Version));
        }

        var tasks = seeded.Select(async item =>
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            return await Budget(context, clock, options).ReconcileAsync(new(
                item.Id, item.Version, new ProviderTokenUsage(40, 20, 60)), default);
        });
        var results = await Task.WhenAll(tasks);

        Assert.All(results, item => Assert.True(item.StateChanged));
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(120, item.ConsumedTotalTokens));
    }

    [Fact]
    public async Task StaleVersion_CannotOverwriteUnresolvedReservation()
    {
        var seeded = await SeedUnresolvedAsync();
        await using var mutationContext = Fixture.DbContextFactory.CreateDbContext();
        var reconciler = Reconciler(mutationContext, seeded.Clock, seeded.Options);
        var owner = Guid.NewGuid();
        _ = await reconciler.ClaimBatchAsync(owner, default);
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Budget(context, seeded.Clock, seeded.Options).ReconcileAsync(new(
                seeded.ReservationId,
                seeded.Version,
                new ProviderTokenUsage(40, 20, 60)), default));
    }

    [Fact]
    public async Task Overage_DeniesSubsequentAdmissionWithoutLosingActualUsage()
    {
        var options = Options(tokenLimit: 200);
        var seeded = await SeedUnresolvedAsync(options);
        await ReconcileAsync(seeded, new(120, 80, 200));
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        var result = await Budget(context, seeded.Clock, options).ReserveAsync(
            Request(seeded.UserId, Guid.NewGuid()), default);

        Assert.NotEqual(AiUsageReservationOutcome.Reserved, result.Outcome);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(200, item.ConsumedTotalTokens));
    }

    [Fact]
    public async Task MissingUsage_RemainsUnresolvedAndKeepsCapacityReserved()
    {
        var clock = new MutableTimeProvider(Now);
        var options = Options();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, clock, options);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid()), default);

        await service.SettleAsync(new(
            reserved.ReservationId!.Value, null, "openai", "model"), default);

        context.ChangeTracker.Clear();
        var entity = await context.AiUsageReservations.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageReconciliationReason.MissingUsage,
            entity.ReconciliationReason);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(150, item.ReservedTokens));
    }

    [Fact]
    public async Task UnknownOutcome_RemainsUnresolvedAndKeepsCapacityReserved()
    {
        var clock = new MutableTimeProvider(Now);
        var options = Options();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, clock, options);
        var reserved = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid()), default);

        await service.MarkReconciliationRequiredAsync(
            reserved.ReservationId!.Value, default);

        context.ChangeTracker.Clear();
        var entity = await context.AiUsageReservations.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageReconciliationReason.UnknownProviderOutcome,
            entity.ReconciliationReason);
        Assert.All(await context.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            item => Assert.Equal(150, item.ReservedTokens));
    }

    [Fact]
    public async Task ExpirationRaceWithSettlement_DoesNotDoubleAccount()
    {
        var clock = new MutableTimeProvider(Now);
        var options = Options();
        Guid id;
        await using (var seed = Fixture.DbContextFactory.CreateDbContext())
        {
            id = (await Budget(seed, clock, options).ReserveAsync(
                Request(Guid.NewGuid(), Guid.NewGuid()), default)).ReservationId!.Value;
        }
        clock.UtcNow = Now.AddHours(1);
        await using var expireContext = Fixture.DbContextFactory.CreateDbContext();
        await using var settleContext = Fixture.DbContextFactory.CreateDbContext();

        await Task.WhenAll(
            Reconciler(expireContext, clock, options)
                .ReconcileExpiredReservationsAsync(default),
            Budget(settleContext, clock, options).SettleAsync(new(
                id, new ProviderTokenUsage(40, 20, 60), null, null), default));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var entity = await verification.AiUsageReservations.AsNoTracking()
            .SingleAsync();
        if (entity.Status == AiUsageReservationStatus.Expired)
        {
            Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
                item => Assert.Equal(150, item.ReservedTokens));
        }
        else
        {
            Assert.Equal(AiUsageReservationStatus.Settled, entity.Status);
            Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
                item => Assert.Equal(60, item.ConsumedTotalTokens));
        }
    }

    [Fact]
    public async Task TwoReplicas_ClaimDifferentReconciliationItems()
    {
        var options = Options(batchSize: 1, minimumAge: TimeSpan.Zero);
        var clock = new MutableTimeProvider(Now);
        await SeedUnresolvedAsync(options, clock);
        await SeedUnresolvedAsync(options, clock);
        await using var first = Fixture.DbContextFactory.CreateDbContext();
        await using var second = Fixture.DbContextFactory.CreateDbContext();

        var claims = await Task.WhenAll(
            Reconciler(first, clock, options).ClaimBatchAsync(Guid.NewGuid(), default),
            Reconciler(second, clock, options).ClaimBatchAsync(Guid.NewGuid(), default));

        Assert.Single(claims[0]);
        Assert.Single(claims[1]);
        Assert.NotEqual(claims[0][0].ReservationId, claims[1][0].ReservationId);
    }

    [Fact]
    public async Task ExpiredClaimLease_CanBeReclaimed()
    {
        var options = Options(minimumAge: TimeSpan.Zero);
        var clock = new MutableTimeProvider(Now);
        var seeded = await SeedUnresolvedAsync(options, clock);
        await using var first = Fixture.DbContextFactory.CreateDbContext();
        var firstClaim = Assert.Single(await Reconciler(first, clock, options)
            .ClaimBatchAsync(Guid.NewGuid(), default));
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        await using var second = Fixture.DbContextFactory.CreateDbContext();

        var replacement = Assert.Single(await Reconciler(second, clock, options)
            .ClaimBatchAsync(Guid.NewGuid(), default));

        Assert.Equal(seeded.ReservationId, replacement.ReservationId);
        Assert.NotEqual(firstClaim.Version, replacement.Version);
    }

    [Fact]
    public async Task StaleClaim_CannotReleaseReplacementLease()
    {
        var options = Options(minimumAge: TimeSpan.Zero);
        var clock = new MutableTimeProvider(Now);
        await SeedUnresolvedAsync(options, clock);
        var firstOwner = Guid.NewGuid();
        await using var first = Fixture.DbContextFactory.CreateDbContext();
        var stale = Assert.Single(await Reconciler(first, clock, options)
            .ClaimBatchAsync(firstOwner, default));
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        await using var second = Fixture.DbContextFactory.CreateDbContext();
        var currentOwner = Guid.NewGuid();
        _ = Assert.Single(await Reconciler(second, clock, options)
            .ClaimBatchAsync(currentOwner, default));

        Assert.False(await Reconciler(first, clock, options)
            .ReleaseClaimAsync(stale, firstOwner, default));
    }

    [Theory]
    [InlineData(AiUsageReservationStatus.Reserved)]
    [InlineData(AiUsageReservationStatus.ReconciliationRequired)]
    [InlineData(AiUsageReservationStatus.Expired)]
    public async Task Purge_NeverDeletesActiveOrUnresolvedReservations(
        AiUsageReservationStatus desiredStatus)
    {
        var options = Options(purge: true);
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, clock, options);
        var reservation = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid()), default);
        if (desiredStatus == AiUsageReservationStatus.ReconciliationRequired)
        {
            await service.MarkReconciliationRequiredAsync(
                reservation.ReservationId!.Value, default);
        }
        else if (desiredStatus == AiUsageReservationStatus.Expired)
        {
            clock.UtcNow = clock.UtcNow.AddHours(1);
            await Reconciler(context, clock, options)
                .ReconcileExpiredReservationsAsync(default);
        }
        clock.UtcNow = clock.UtcNow.AddDays(100);

        Assert.Equal(0, await Reconciler(context, clock, options)
            .PurgeResolvedReservationsAsync(default));
        Assert.Equal(1, await context.AiUsageReservations.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Purge_DeletesOldSettledAndReleasedReservations(bool settled)
    {
        var options = Options(purge: true);
        var clock = new MutableTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, clock, options);
        var reservation = await service.ReserveAsync(
            Request(Guid.NewGuid(), Guid.NewGuid()), default);
        if (settled)
        {
            await service.SettleAsync(new(
                reservation.ReservationId!.Value,
                new ProviderTokenUsage(40, 20, 60), null, null), default);
        }
        else
        {
            await service.ReleaseAsync(reservation.ReservationId!.Value, default);
        }
        clock.UtcNow = clock.UtcNow.AddDays(100);

        Assert.Equal(1, await Reconciler(context, clock, options)
            .PurgeResolvedReservationsAsync(default));
        Assert.Empty(await context.AiUsageReservations.ToListAsync());
        Assert.Equal(4, await context.AiUsageBudgetPeriods.CountAsync());
    }

    [Fact]
    public async Task Purge_IsBoundedByConfiguredBatchSize()
    {
        var options = Options(purge: true, purgeBatchSize: 1);
        var clock = new MutableTimeProvider(Now);
        for (var index = 0; index < 2; index++)
        {
            await using var seed = Fixture.DbContextFactory.CreateDbContext();
            var service = Budget(seed, clock, options);
            var result = await service.ReserveAsync(
                Request(Guid.NewGuid(), Guid.NewGuid()), default);
            await service.ReleaseAsync(result.ReservationId!.Value, default);
        }
        clock.UtcNow = clock.UtcNow.AddDays(100);
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        Assert.Equal(1, await Reconciler(context, clock, options)
            .PurgeResolvedReservationsAsync(default));
        Assert.Equal(1, await context.AiUsageReservations.CountAsync());
    }

    [Fact]
    public async Task TwoPurgeReplicas_DeleteEachResolvedReservationOnlyOnce()
    {
        var options = Options(purge: true, purgeBatchSize: 1);
        var clock = new MutableTimeProvider(Now);
        for (var index = 0; index < 2; index++)
        {
            await using var seed = Fixture.DbContextFactory.CreateDbContext();
            var service = Budget(seed, clock, options);
            var result = await service.ReserveAsync(
                Request(Guid.NewGuid(), Guid.NewGuid()), default);
            await service.ReleaseAsync(result.ReservationId!.Value, default);
        }
        clock.UtcNow = clock.UtcNow.AddDays(100);
        await using var first = Fixture.DbContextFactory.CreateDbContext();
        await using var second = Fixture.DbContextFactory.CreateDbContext();

        var counts = await Task.WhenAll(
            Reconciler(first, clock, options).PurgeResolvedReservationsAsync(default),
            Reconciler(second, clock, options).PurgeResolvedReservationsAsync(default));

        Assert.Equal(2, counts.Sum());
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Empty(await verification.AiUsageReservations.ToListAsync());
    }

    [Fact]
    public async Task SettlementFailure_RollsBackEveryPeriodCounterMutation()
    {
        var seeded = await SeedUnresolvedAsync();
        await using var setup = Fixture.DbContextFactory.CreateDbContext();
        await setup.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION resumatch_test_fail_reconciliation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'controlled reconciliation failure';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER resumatch_test_fail_reconciliation_trigger
            BEFORE UPDATE ON "AiUsageReservations"
            FOR EACH ROW
            WHEN (NEW."Status" = 1)
            EXECUTE FUNCTION resumatch_test_fail_reconciliation();
            """);
        try
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                Budget(context, seeded.Clock, seeded.Options).ReconcileAsync(new(
                    seeded.ReservationId,
                    seeded.Version,
                    new ProviderTokenUsage(40, 20, 60)), default));

            await using var verification = Fixture.DbContextFactory.CreateDbContext();
            Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
                item =>
                {
                    Assert.Equal(150, item.ReservedTokens);
                    Assert.Equal(0, item.ConsumedTotalTokens);
                });
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                DROP TRIGGER IF EXISTS resumatch_test_fail_reconciliation_trigger
                ON "AiUsageReservations";
                DROP FUNCTION IF EXISTS resumatch_test_fail_reconciliation();
                """);
        }
    }

    [Fact]
    public async Task RawInvalidReconciliationReason_IsRejectedByPostgreSql()
    {
        var seeded = await SeedUnresolvedAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "UPDATE \"AiUsageReservations\" SET \"ReconciliationReason\" = 99 WHERE \"Id\" = {0}",
                seeded.ReservationId));
    }

    [Fact]
    public async Task RawInconsistentOverage_IsRejectedByPostgreSql()
    {
        var seeded = await SeedUnresolvedAsync();
        await ReconcileAsync(seeded, new(120, 80, 200));
        await using var context = Fixture.DbContextFactory.CreateDbContext();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "UPDATE \"AiUsageReservations\" SET \"OverageTotalTokens\" = 0 WHERE \"Id\" = {0}",
                seeded.ReservationId));
    }

    private async Task<Seeded> SeedUnresolvedAsync(
        AiUsageBudgetOptions? options = null,
        MutableTimeProvider? clock = null)
    {
        options ??= Options();
        clock ??= new MutableTimeProvider(Now);
        var userId = Guid.NewGuid();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = Budget(context, clock, options);
        var reservation = await service.ReserveAsync(
            Request(userId, Guid.NewGuid()), default);
        await service.MarkReconciliationRequiredAsync(
            reservation.ReservationId!.Value, default);
        context.ChangeTracker.Clear();
        var entity = await context.AiUsageReservations.AsNoTracking()
            .SingleAsync(item => item.Id == reservation.ReservationId);
        return new(entity.Id, entity.Version, userId, clock, options);
    }

    private async Task ReconcileAsync(Seeded seeded, ProviderTokenUsage usage)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await Budget(context, seeded.Clock, seeded.Options).ReconcileAsync(new(
            seeded.ReservationId, seeded.Version, usage), default);
    }

    private static AiUsageReservationRequest Request(Guid userId, Guid draftId) =>
        new(userId, draftId, 1);

    private static AiUsageBudgetService Budget(
        AppDbContext context,
        TimeProvider clock,
        AiUsageBudgetOptions options) =>
        new(context, Microsoft.Extensions.Options.Options.Create(options), clock);

    private static AiUsageReservationReconciliationService Reconciler(
        AppDbContext context,
        TimeProvider clock,
        AiUsageBudgetOptions options) =>
        new(context, clock, Microsoft.Extensions.Options.Options.Create(options));

    private static AiUsageBudgetOptions Options(
        long tokenLimit = 10_000,
        int batchSize = 10,
        TimeSpan? minimumAge = null,
        bool purge = false,
        int purgeBatchSize = 100) => new()
    {
        Enabled = true,
        User = new()
        {
            DailyRequestLimit = 20,
            MonthlyRequestLimit = 20,
            DailyTokenLimit = tokenLimit,
            MonthlyTokenLimit = tokenLimit
        },
        System = new()
        {
            DailyRequestLimit = 100,
            MonthlyRequestLimit = 100,
            DailyTokenLimit = tokenLimit,
            MonthlyTokenLimit = tokenLimit
        },
        Reservation = new()
        {
            EstimatedInputTokens = 100,
            MaximumOutputTokens = 50,
            Lifetime = TimeSpan.FromMinutes(30)
        },
        Reconciliation = new()
        {
            Enabled = true,
            BatchSize = batchSize,
            PollInterval = TimeSpan.FromMinutes(5),
            MinimumAge = minimumAge ?? TimeSpan.Zero,
            LeaseDuration = TimeSpan.FromMinutes(5),
            Purge = new()
            {
                Enabled = purge,
                RetentionPeriod = TimeSpan.FromDays(90),
                BatchSize = purgeBatchSize,
                PollInterval = TimeSpan.FromHours(1)
            }
        }
    };

    private sealed record Seeded(
        Guid ReservationId,
        uint Version,
        Guid UserId,
        MutableTimeProvider Clock,
        AiUsageBudgetOptions Options);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
