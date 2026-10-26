using System.Data;
using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing.Budget;

public sealed class AiUsageReservationReconciliationService :
    IAiUsageReservationReconciliationService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly AiUsageBudgetOptions _options;

    public AiUsageReservationReconciliationService(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<AiUsageBudgetOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<int> ReconcileExpiredReservationsAsync(
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var now = GetUtcNow();
        var count = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH candidates AS
            (
                SELECT "Id"
                FROM "AiUsageReservations"
                WHERE "Status" = {(int)AiUsageReservationStatus.Reserved}
                  AND "ExpiresAt" <= {now}
                ORDER BY "ExpiresAt" ASC, "Id" ASC
                FOR UPDATE SKIP LOCKED
                LIMIT {_options.Reconciliation.BatchSize}
            )
            UPDATE "AiUsageReservations" AS reservation
            SET "Status" = {(int)AiUsageReservationStatus.Expired},
                "ReconciliationRequiredAt" = {now},
                "ReconciliationReason" = {(int)AiUsageReconciliationReason.ExpiredUnknownOutcome},
                "ReconciliationLeaseOwner" = NULL,
                "ReconciliationLeaseExpiresAt" = NULL,
                "UpdatedAt" = {now}
            FROM candidates
            WHERE reservation."Id" = candidates."Id";
            """, cancellationToken);
        if (count > 0)
        {
            JobPostingParseDraftMetrics.BudgetReconciled.Add(count);
        }

        return count;
    }

    public async Task<IReadOnlyList<AiUsageReconciliationClaim>> ClaimBatchAsync(
        Guid leaseOwner,
        CancellationToken cancellationToken)
    {
        if (leaseOwner == Guid.Empty)
        {
            throw new ArgumentException("A reconciliation lease owner is required.",
                nameof(leaseOwner));
        }

        if (!_options.Enabled)
        {
            return [];
        }

        var now = GetUtcNow();
        var eligibleBefore = now.Subtract(_options.Reconciliation.MinimumAge);
        var leaseExpiresAt = now.Add(_options.Reconciliation.LeaseDuration);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            WITH candidates AS MATERIALIZED
            (
                SELECT "Id"
                FROM "AiUsageReservations"
                WHERE "Status" IN (3, 4)
                  AND "ReconciliationRequiredAt" <= @eligibleBefore
                  AND ("ReconciliationLeaseExpiresAt" IS NULL OR
                       "ReconciliationLeaseExpiresAt" <= @now)
                ORDER BY "ReconciliationRequiredAt" ASC, "Id" ASC
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            ), claimed AS
            (
                UPDATE "AiUsageReservations" AS reservation
                SET "ReconciliationLeaseOwner" = @leaseOwner,
                    "ReconciliationLeaseExpiresAt" = @leaseExpiresAt,
                    "UpdatedAt" = @now
                FROM candidates
                WHERE reservation."Id" = candidates."Id"
                RETURNING reservation."Id", reservation.xmin,
                          reservation."Status", reservation."ReconciliationReason",
                          reservation."ReconciliationRequiredAt",
                          reservation."ReconciliationLeaseExpiresAt"
            )
            SELECT * FROM claimed;
            """;
        AddParameter(command, "eligibleBefore", eligibleBefore);
        AddParameter(command, "now", now);
        AddParameter(command, "batchSize", _options.Reconciliation.BatchSize);
        AddParameter(command, "leaseOwner", leaseOwner);
        AddParameter(command, "leaseExpiresAt", leaseExpiresAt);

        var claims = new List<AiUsageReconciliationClaim>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(new(
                reader.GetGuid(0),
                reader.GetFieldValue<uint>(1),
                (AiUsageReservationStatus)reader.GetInt32(2),
                (AiUsageReconciliationReason)reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        if (claims.Count > 0)
        {
            JobPostingParseDraftMetrics.BudgetReconciliationClaimed.Add(claims.Count);
        }

        return claims
            .OrderBy(item => item.RequiredAt)
            .ThenBy(item => item.ReservationId)
            .ToArray();
    }

    public async Task<bool> ReleaseClaimAsync(
        AiUsageReconciliationClaim claim,
        Guid leaseOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (leaseOwner == Guid.Empty)
        {
            throw new ArgumentException("A reconciliation lease owner is required.",
                nameof(leaseOwner));
        }

        var now = GetUtcNow();
        var changed = await _dbContext.AiUsageReservations
            .Where(item =>
                item.Id == claim.ReservationId &&
                item.Version == claim.Version &&
                item.ReconciliationLeaseOwner == leaseOwner &&
                item.Status == claim.Status)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ReconciliationLeaseOwner, (Guid?)null)
                .SetProperty(item => item.ReconciliationLeaseExpiresAt,
                    (DateTimeOffset?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        return changed == 1;
    }

    public async Task<int> PurgeResolvedReservationsAsync(
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.Reconciliation.Purge.Enabled)
        {
            return 0;
        }

        var cutoff = GetUtcNow().Subtract(
            _options.Reconciliation.Purge.RetentionPeriod);
        var count = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH candidates AS
            (
                SELECT "Id"
                FROM "AiUsageReservations"
                WHERE ("Status" = {(int)AiUsageReservationStatus.Settled}
                       AND "SettledAt" <= {cutoff})
                   OR ("Status" = {(int)AiUsageReservationStatus.Released}
                       AND "ReleasedAt" <= {cutoff})
                ORDER BY COALESCE("SettledAt", "ReleasedAt") ASC, "Id" ASC
                FOR UPDATE SKIP LOCKED
                LIMIT {_options.Reconciliation.Purge.BatchSize}
            )
            DELETE FROM "AiUsageReservations" AS reservation
            USING candidates
            WHERE reservation."Id" = candidates."Id";
            """, cancellationToken);
        JobPostingParseDraftMetrics.BudgetLedgerPurgeBatches.Add(1);
        if (count > 0)
        {
            JobPostingParseDraftMetrics.BudgetLedgerPurgedReservations.Add(count);
        }

        return count;
    }

    private DateTimeOffset GetUtcNow() =>
        _timeProvider.GetUtcNow().ToUniversalTime();

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
