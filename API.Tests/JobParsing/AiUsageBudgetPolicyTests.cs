using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class AiUsageBudgetPolicyTests
{
    [Fact]
    public void DailyPeriod_UsesUtcMidnightAcrossOffset()
    {
        var result = AiUsageBudgetPeriodCalculator.GetDaily(
            new DateTimeOffset(2026, 10, 3, 2, 30, 0, TimeSpan.FromHours(3)));

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), result.Start);
        Assert.Equal(result.Start.AddDays(1), result.End);
    }

    [Fact]
    public void MonthlyPeriod_HandlesYearBoundaryInUtc()
    {
        var result = AiUsageBudgetPeriodCalculator.GetMonthly(
            new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), result.Start);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), result.End);
    }

    [Fact]
    public void DailyPeriod_HandlesLeapDay()
    {
        var result = AiUsageBudgetPeriodCalculator.GetDaily(
            new DateTimeOffset(2028, 2, 29, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), result.End);
    }

    [Fact]
    public void ReservationAmount_UsesCheckedSum()
    {
        var result = AiUsageReservationAmount.Create(120, 80);

        Assert.Equal(1, result.RequestCount);
        Assert.Equal(200, result.TotalTokens);
        Assert.Throws<OverflowException>(() =>
            AiUsageReservationAmount.Create(long.MaxValue, 1));
    }

    [Fact]
    public void OptionsValidator_RejectsMonthlyLimitBelowDailyLimit()
    {
        var validator = new AiUsageBudgetOptionsValidator();
        var options = new AiUsageBudgetOptions
        {
            User = new AiUsageBudgetLimitOptions
            {
                DailyRequestLimit = 10,
                MonthlyRequestLimit = 9,
                DailyTokenLimit = 100,
                MonthlyTokenLimit = 1_000
            }
        };

        Assert.True(validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(AiUsageReservationStatus.Settled)]
    [InlineData(AiUsageReservationStatus.Released)]
    [InlineData(AiUsageReservationStatus.Expired)]
    [InlineData(AiUsageReservationStatus.ReconciliationRequired)]
    public void Reserved_AllowsOnlyDefinedTerminalTransitions(
        AiUsageReservationStatus target)
    {
        Assert.True(AiUsageReservationTransitionPolicy.CanTransition(
            AiUsageReservationStatus.Reserved, target));
        Assert.False(AiUsageReservationTransitionPolicy.CanTransition(target, target));
    }

    [Fact]
    public void UsagePolicy_AcceptsOnlyCompleteConsistentUsage()
    {
        Assert.True(AiUsageProviderUsagePolicy.TryValidate(
            new ProviderTokenUsage(10, 5, 15), out _, out _, out _));
        Assert.False(AiUsageProviderUsagePolicy.TryValidate(
            new ProviderTokenUsage(10, null, 10), out _, out _, out _));
        Assert.False(AiUsageProviderUsagePolicy.TryValidate(
            new ProviderTokenUsage(10, 5, 99), out _, out _, out _));
        Assert.False(AiUsageProviderUsagePolicy.TryValidate(
            new ProviderTokenUsage(-1, 5, 4), out _, out _, out _));
    }

    [Theory]
    [InlineData(AiUsageReservationStatus.ReconciliationRequired)]
    [InlineData(AiUsageReservationStatus.Expired)]
    public void UnresolvedReservation_AllowsOnlySettlement(
        AiUsageReservationStatus current)
    {
        Assert.True(AiUsageReservationTransitionPolicy.CanTransition(
            current, AiUsageReservationStatus.Settled));
        Assert.False(AiUsageReservationTransitionPolicy.CanTransition(
            current, AiUsageReservationStatus.Released));
        Assert.False(AiUsageReservationTransitionPolicy.CanTransition(
            current, AiUsageReservationStatus.Reserved));
    }

    [Theory]
    [InlineData(AiUsageReservationStatus.Settled)]
    [InlineData(AiUsageReservationStatus.Released)]
    public void ResolvedTerminalReservation_AllowsNoFurtherTransition(
        AiUsageReservationStatus current)
    {
        foreach (var target in Enum.GetValues<AiUsageReservationStatus>())
        {
            Assert.False(AiUsageReservationTransitionPolicy.CanTransition(
                current, target));
        }
    }

    [Theory]
    [InlineData(AiUsageReservationStatus.Reserved)]
    [InlineData(AiUsageReservationStatus.ReconciliationRequired)]
    [InlineData(AiUsageReservationStatus.Expired)]
    public void RetentionPolicy_NeverPurgesUnresolvedState(
        AiUsageReservationStatus status)
    {
        Assert.False(AiUsageLedgerRetentionPolicy.CanPurge(
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RetentionPolicy_RequiresResolvedTimestampBeforeCutoff()
    {
        var cutoff = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(AiUsageLedgerRetentionPolicy.CanPurge(
            AiUsageReservationStatus.Settled,
            cutoff.AddTicks(-1),
            null,
            cutoff));
        Assert.False(AiUsageLedgerRetentionPolicy.CanPurge(
            AiUsageReservationStatus.Settled,
            cutoff.AddTicks(1),
            null,
            cutoff));
    }

    [Theory]
    [InlineData(-1, 5, 5, 90)]
    [InlineData(10, 0, 5, 90)]
    [InlineData(10, 5, 0, 90)]
    [InlineData(10, 5, 5, 0)]
    public void OptionsValidator_RejectsInvalidReconciliationAndPurgeValues(
        int minimumAgeMinutes,
        int leaseMinutes,
        int purgeBatchSize,
        int retentionDays)
    {
        var validator = new AiUsageBudgetOptionsValidator();
        var options = new AiUsageBudgetOptions
        {
            Reconciliation = new AiUsageReconciliationOptions
            {
                MinimumAge = TimeSpan.FromMinutes(minimumAgeMinutes),
                LeaseDuration = TimeSpan.FromMinutes(leaseMinutes),
                Purge = new AiUsageLedgerPurgeOptions
                {
                    BatchSize = purgeBatchSize,
                    RetentionPeriod = TimeSpan.FromDays(retentionDays)
                }
            }
        };

        Assert.True(validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(AiUsageReconciliationReason.MissingUsage)]
    [InlineData(AiUsageReconciliationReason.UnknownProviderOutcome)]
    [InlineData(AiUsageReconciliationReason.ExpiredUnknownOutcome)]
    [InlineData(AiUsageReconciliationReason.ActualExceedsReserved)]
    public void ReconciliationReasons_AreBoundedEnumValues(
        AiUsageReconciliationReason reason)
    {
        Assert.True(Enum.IsDefined(reason));
    }

    [Theory]
    [InlineData(40, 20, 60, 0, 0, 0)]
    [InlineData(100, 50, 150, 0, 0, 0)]
    [InlineData(120, 80, 200, 20, 30, 50)]
    public void OveragePolicy_PreservesBelowEqualAndAboveReservationSemantics(
        long input,
        long output,
        long total,
        long overageInput,
        long overageOutput,
        long overageTotal)
    {
        var result = AiUsageOverage.Calculate(
            100, 50, 150, input, output, total);

        Assert.Equal(overageInput, result.InputTokens);
        Assert.Equal(overageOutput, result.OutputTokens);
        Assert.Equal(overageTotal, result.TotalTokens);
    }
}
