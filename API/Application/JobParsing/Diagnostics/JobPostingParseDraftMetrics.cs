using System.Diagnostics.Metrics;
using Api.Entities.Enums;

namespace Api.Application.JobParsing.Diagnostics;

public static class JobPostingParseDraftMetrics
{
    public const string MeterName = "ResuMatch.JobParsing";
    public const string InstrumentationVersion = "1.0.0";

    private static long _parseQueueBacklog;
    private static long _parseOldestAgeBits;
    private static long _reconciliationOldestAgeBits;
    private static readonly long[] ReconciliationBacklogByReason =
        new long[Enum.GetValues<AiUsageReconciliationReason>().Length];

    private static readonly Meter Meter = new(MeterName, InstrumentationVersion);

    public static readonly Counter<long> Claimed =
        Meter.CreateCounter<long>("resumatch.job_parsing.claimed");

    public static readonly Counter<long> Ready =
        Meter.CreateCounter<long>("resumatch.job_parsing.ready");

    public static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("resumatch.job_parsing.failed");

    public static readonly Counter<long> Retried =
        Meter.CreateCounter<long>("resumatch.job_parsing.retried");

    public static readonly Counter<long> Superseded =
        Meter.CreateCounter<long>("resumatch.job_parsing.superseded");

    public static readonly Counter<long> ProviderRequests =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.requests");

    public static readonly Counter<long> ProviderInputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.input_tokens");

    public static readonly Counter<long> ProviderOutputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.output_tokens");

    public static readonly Counter<long> ProviderTotalTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.total_tokens");

    public static readonly Counter<long> ProviderRateLimited =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.rate_limited");

    public static readonly Counter<long> ProviderUnavailable =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.unavailable");

    public static readonly Counter<long> ProviderRefused =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.refused");

    public static readonly Counter<long> ProviderInvalidResponses =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.invalid_response");

    public static readonly Counter<long> ProviderTimeouts =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.timeouts");

    public static readonly Histogram<double> ProviderLatency =
        Meter.CreateHistogram<double>(
            "resumatch.job_parsing.provider.duration",
            unit: "ms");

    public static readonly Counter<long> PurgeBatches =
        Meter.CreateCounter<long>("resumatch.job_parsing.purge.batches");

    public static readonly Counter<long> PurgedDrafts =
        Meter.CreateCounter<long>("resumatch.job_parsing.purge.drafts");

    public static readonly Counter<long> PurgeFailures =
        Meter.CreateCounter<long>("resumatch.job_parsing.purge.failures");

    public static readonly Counter<long> BudgetReservations =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reservations");

    public static readonly Counter<long> BudgetDenied =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.denied");

    public static readonly Counter<long> BudgetSettled =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.settled");

    public static readonly Counter<long> BudgetReleased =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.released");

    public static readonly Counter<long> BudgetReconciliationRequired =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciliation_required");

    public static readonly Counter<long> BudgetReconciled =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciled");

    public static readonly Counter<long> BudgetReconciliationClaimed =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciliation.claimed");

    public static readonly Counter<long> BudgetReconciliationOverage =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciliation.overage");

    public static readonly Counter<long> BudgetReconciliationSettled =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciliation.settled");

    public static readonly Counter<long> BudgetOverageInputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.overage.input_tokens");

    public static readonly Counter<long> BudgetOverageOutputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.overage.output_tokens");

    public static readonly Counter<long> BudgetOverageTotalTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.overage.total_tokens");

    public static readonly Counter<long> BudgetReconciliationFailures =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.reconciliation.failures");

    public static readonly Counter<long> BudgetLedgerPurgeBatches =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.ledger.purge.batches");

    public static readonly Counter<long> BudgetLedgerPurgedReservations =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.ledger.purge.reservations");

    public static readonly Counter<long> BudgetLedgerPurgeFailures =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.ledger.purge.failures");

    public static readonly Counter<long> BudgetInputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.input_tokens");

    public static readonly Counter<long> BudgetOutputTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.output_tokens");

    public static readonly Counter<long> BudgetTotalTokens =
        Meter.CreateCounter<long>("resumatch.job_parsing.budget.total_tokens");

    public static readonly Counter<long> DistributedSlotsAcquired =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.distributed_slot.acquired");
    public static readonly Counter<long> DistributedSlotsDenied =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.distributed_slot.denied");
    public static readonly Counter<long> DistributedSlotsReclaimed =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.distributed_slot.reclaimed");
    public static readonly Counter<long> CircuitOpened =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.opened");
    public static readonly Counter<long> CircuitClosed =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.closed");
    public static readonly Counter<long> CircuitRejected =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.rejected");
    public static readonly Counter<long> CircuitHalfOpenProbes =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.half_open_probe");
    public static readonly Counter<long> CircuitHalfOpenSuccess =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.half_open_success");
    public static readonly Counter<long> CircuitHalfOpenFailure =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.half_open_failure");

    public static readonly Counter<long> CircuitProbeDenied =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.probe_denied");

    public static readonly Counter<long> CircuitProbeRecovered =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.circuit.probe_recovered");

    public static readonly Counter<long> WorkerIterationFailures =
        Meter.CreateCounter<long>("resumatch.job_parsing.worker.iteration_failures");

    public static readonly Counter<long> ProviderExecutionDisabled =
        Meter.CreateCounter<long>("resumatch.job_parsing.provider.execution_disabled");

    public static readonly Counter<long> ReadinessFailures =
        Meter.CreateCounter<long>("resumatch.readiness.failures");

    public static readonly Counter<long> OperationalSamplerFailures =
        Meter.CreateCounter<long>("resumatch.job_parsing.operational_sampler.failures");

    private static readonly ObservableGauge<long> ParseQueueBacklog =
        Meter.CreateObservableGauge(
            "resumatch.job_parsing.queue.backlog",
            () => Volatile.Read(ref _parseQueueBacklog));

    private static readonly ObservableGauge<double> ParseQueueOldestAge =
        Meter.CreateObservableGauge(
            "resumatch.job_parsing.queue.oldest_age",
            () => BitConverter.Int64BitsToDouble(
                Volatile.Read(ref _parseOldestAgeBits)),
            unit: "s");

    private static readonly ObservableGauge<long> ReconciliationQueueBacklog =
        Meter.CreateObservableGauge(
            "resumatch.job_parsing.budget.reconciliation.queue.backlog",
            ObserveReconciliationBacklog);

    private static readonly ObservableGauge<double> ReconciliationOldestAge =
        Meter.CreateObservableGauge(
            "resumatch.job_parsing.budget.reconciliation.queue.oldest_age",
            () => BitConverter.Int64BitsToDouble(
                Volatile.Read(ref _reconciliationOldestAgeBits)),
            unit: "s");

    public static void SetOperationalSnapshot(
        long parseQueueBacklog,
        double parseOldestAgeSeconds,
        IReadOnlyDictionary<AiUsageReconciliationReason, long>
            reconciliationBacklog,
        double reconciliationOldestAgeSeconds)
    {
        Interlocked.Exchange(ref _parseQueueBacklog, Math.Max(0, parseQueueBacklog));
        Interlocked.Exchange(
            ref _parseOldestAgeBits,
            BitConverter.DoubleToInt64Bits(Math.Max(0, parseOldestAgeSeconds)));
        Interlocked.Exchange(
            ref _reconciliationOldestAgeBits,
            BitConverter.DoubleToInt64Bits(
                Math.Max(0, reconciliationOldestAgeSeconds)));

        foreach (var reason in Enum.GetValues<AiUsageReconciliationReason>())
        {
            var value = reconciliationBacklog.GetValueOrDefault(reason);
            Interlocked.Exchange(
                ref ReconciliationBacklogByReason[(int)reason],
                Math.Max(0, value));
        }
    }

    private static IEnumerable<Measurement<long>> ObserveReconciliationBacklog()
    {
        foreach (var reason in Enum.GetValues<AiUsageReconciliationReason>())
        {
            yield return new Measurement<long>(
                Volatile.Read(ref ReconciliationBacklogByReason[(int)reason]),
                new KeyValuePair<string, object?>("reason", reason.ToString()));
        }
    }
}
