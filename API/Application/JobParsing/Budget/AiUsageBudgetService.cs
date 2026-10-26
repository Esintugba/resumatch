using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Api.Application.JobParsing.Budget;

public sealed class AiUsageBudgetService : IAiUsageBudgetService
{
    private const int MaximumProviderNameLength = 100;
    private const int MaximumModelNameLength = 200;
    private const int MaximumProviderRequestIdLength = 200;

    private readonly AppDbContext _dbContext;
    private readonly AiUsageBudgetOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiUsageBudgetService> _logger;

    public AiUsageBudgetService(
        AppDbContext dbContext,
        IOptions<AiUsageBudgetOptions> options,
        TimeProvider timeProvider,
        ILogger<AiUsageBudgetService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<AiUsageBudgetService>.Instance;
    }

    public async Task<AiUsageReservationResult> ReserveAsync(
        AiUsageReservationRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.budget.reserve");
        ValidateRequest(request);
        if (!_options.Enabled)
        {
            return new(AiUsageReservationOutcome.Disabled);
        }

        var now = GetUtcNow();
        var amount = AiUsageReservationAmount.Create(
            _options.Reservation.EstimatedInputTokens,
            _options.Reservation.MaximumOutputTokens);
        var daily = AiUsageBudgetPeriodCalculator.GetDaily(now);
        var monthly = AiUsageBudgetPeriodCalculator.GetMonthly(now);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await EnsurePeriodAsync(AiUsageBudgetScopeType.System, null, daily,
            _options.System.DailyRequestLimit, _options.System.DailyTokenLimit,
            now, transaction, cancellationToken);
        await EnsurePeriodAsync(AiUsageBudgetScopeType.System, null, monthly,
            _options.System.MonthlyRequestLimit, _options.System.MonthlyTokenLimit,
            now, transaction, cancellationToken);
        await EnsurePeriodAsync(AiUsageBudgetScopeType.User, request.UserId, daily,
            _options.User.DailyRequestLimit, _options.User.DailyTokenLimit,
            now, transaction, cancellationToken);
        await EnsurePeriodAsync(AiUsageBudgetScopeType.User, request.UserId, monthly,
            _options.User.MonthlyRequestLimit, _options.User.MonthlyTokenLimit,
            now, transaction, cancellationToken);

        var periodIds = await FindPeriodIdsAsync(
            request.UserId, daily.Start, monthly.Start, cancellationToken);
        if (periodIds.Count != 4)
        {
            throw new InvalidOperationException("The required AI budget periods could not be loaded.");
        }

        await LockPeriodsAsync(periodIds, transaction, cancellationToken);

        var existing = await _dbContext.AiUsageReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.DraftId == request.DraftId &&
                    item.AttemptCount == request.AttemptCount,
                cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(AiUsageReservationOutcome.Duplicate, existing.Id);
        }

        var periods = await _dbContext.AiUsageBudgetPeriods
            .Where(item => periodIds.Contains(item.Id))
            .OrderBy(item => item.ScopeType)
            .ThenBy(item => item.UserId)
            .ThenBy(item => item.PeriodType)
            .ThenBy(item => item.PeriodStart)
            .ToListAsync(cancellationToken);

        var failed = periods.Where(period =>
                amount.RequestCount > period.RequestLimit -
                    period.ReservedRequests - period.ConsumedRequests ||
                amount.TotalTokens > period.TokenLimit -
                    period.ReservedTokens - period.ConsumedTotalTokens)
            .ToArray();
        if (failed.Length > 0)
        {
            var userFailure = failed.Any(item => item.ScopeType == AiUsageBudgetScopeType.User);
            var nextEligibleAt = failed.Max(item => item.PeriodEnd);
            await transaction.CommitAsync(cancellationToken);
            var deniedScope = userFailure ? "user" : "system";
            JobPostingParseDraftMetrics.BudgetDenied.Add(
                1,
                new KeyValuePair<string, object?>("scope", deniedScope));
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("budget.scope", deniedScope);
            activity?.SetTag("budget.outcome", "denied");
            _logger.LogInformation(
                "AI usage budget reservation denied. Operation={Operation} Scope={Scope} NextEligibleAt={NextEligibleAt}",
                "reserve", userFailure ? "user" : "system", nextEligibleAt);
            return new(
                userFailure
                    ? AiUsageReservationOutcome.UserBudgetExceeded
                    : AiUsageReservationOutcome.SystemBudgetExceeded,
                NextEligibleAt: nextEligibleAt);
        }

        var reservation = new AiUsageReservation
        {
            UserId = request.UserId,
            DraftId = request.DraftId,
            AttemptCount = request.AttemptCount,
            AttemptKey = $"{request.DraftId:N}:{request.AttemptCount}",
            ExpiresAt = now.Add(_options.Reservation.Lifetime),
            ReservedRequestCount = amount.RequestCount,
            ReservedInputTokens = amount.InputTokens,
            ReservedOutputTokens = amount.OutputTokens,
            ReservedTotalTokens = amount.TotalTokens,
            Status = AiUsageReservationStatus.Reserved,
            CreatedAt = now
        };

        foreach (var period in periods)
        {
            period.ReservedRequests += amount.RequestCount;
            period.ReservedTokens += amount.TotalTokens;
            period.UpdatedAt = now;
            reservation.Periods.Add(new AiUsageReservationPeriod
            {
                BudgetPeriod = period,
                ReservedRequestCount = amount.RequestCount,
                ReservedTokenCount = amount.TotalTokens
            });
        }

        _dbContext.AiUsageReservations.Add(reservation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        JobPostingParseDraftMetrics.BudgetReservations.Add(1);
        activity?.SetTag("budget.outcome", "reserved");
        return new(AiUsageReservationOutcome.Reserved, reservation.Id);
    }

    public async Task<AiUsageSettlementResult> SettleAsync(
        AiUsageSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ReservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation identifier is required.", nameof(request));
        }

        if (!_options.Enabled)
        {
            return new(AiUsageReservationStatus.Released, false);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockReservationAsync(request.ReservationId, transaction, cancellationToken);
        var reservation = await LoadReservationAsync(request.ReservationId, cancellationToken);
        if (reservation.Status is AiUsageReservationStatus.Settled or
            AiUsageReservationStatus.Released)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(reservation.Status, false);
        }

        reservation.ProviderName = NormalizeMetadata(request.ProviderName, MaximumProviderNameLength);
        reservation.ModelName = NormalizeMetadata(request.ModelName, MaximumModelNameLength);
        reservation.ProviderRequestId = NormalizeMetadata(
            request.ProviderRequestId, MaximumProviderRequestIdLength);

        var validUsage = AiUsageProviderUsagePolicy.TryValidate(
            request.Usage, out var input, out var output, out var total);
        if (!validUsage)
        {
            if (reservation.Status is AiUsageReservationStatus.Expired or
                AiUsageReservationStatus.ReconciliationRequired)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(reservation.Status, false);
            }

            ApplyReconciliationRequired(
                reservation,
                GetUtcNow(),
                AiUsageReconciliationReason.MissingUsage);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            JobPostingParseDraftMetrics.BudgetReconciliationRequired.Add(1);
            return new(reservation.Status, true);
        }

        await ApplySettlementAsync(
            reservation, input, output, total, transaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        RecordSettlementMetrics(
            input,
            output,
            total,
            reservation.OverageInputTokens,
            reservation.OverageOutputTokens,
            reservation.OverageTotalTokens);
        return new(reservation.Status, true);
    }

    public Task<AiUsageSettlementResult> ReleaseAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        ChangeReservedStateAsync(
            reservationId,
            AiUsageReservationStatus.Released,
            releaseCapacity: true,
            cancellationToken);

    public Task<AiUsageSettlementResult> MarkReconciliationRequiredAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        ChangeReservedStateAsync(
            reservationId,
            AiUsageReservationStatus.ReconciliationRequired,
            releaseCapacity: false,
            cancellationToken);

    public async Task<AiUsageSettlementResult> ReconcileAsync(
        AiUsageReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.budget.reconcile");
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReservationId == Guid.Empty || request.ExpectedVersion == 0 ||
            !AiUsageProviderUsagePolicy.TryValidate(
                request.Usage, out var input, out var output, out var total))
        {
            throw new ArgumentException(
                "A reservation, version and complete usage are required.",
                nameof(request));
        }

        if (!_options.Enabled)
        {
            return new(AiUsageReservationStatus.ReconciliationRequired, false);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockReservationAsync(request.ReservationId, transaction, cancellationToken);
        var reservation = await LoadReservationAsync(
            request.ReservationId, cancellationToken);
        activity?.SetTag(
            "reconciliation.reason",
            reservation.ReconciliationReason?.ToString() ?? "none");
        if (reservation.Status is not (
            AiUsageReservationStatus.ReconciliationRequired or
            AiUsageReservationStatus.Expired))
        {
            await transaction.CommitAsync(cancellationToken);
            return new(reservation.Status, false);
        }

        if (reservation.Version != request.ExpectedVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The AI usage reservation changed before reconciliation.");
        }

        _dbContext.Entry(reservation)
            .Property(item => item.Version).OriginalValue = request.ExpectedVersion;
        reservation.ProviderName = NormalizeMetadata(
            request.ProviderName, MaximumProviderNameLength) ?? reservation.ProviderName;
        reservation.ModelName = NormalizeMetadata(
            request.ModelName, MaximumModelNameLength) ?? reservation.ModelName;
        reservation.ProviderRequestId = NormalizeMetadata(
            request.ProviderRequestId, MaximumProviderRequestIdLength) ??
            reservation.ProviderRequestId;

        await ApplySettlementAsync(
            reservation, input, output, total, transaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        RecordSettlementMetrics(
            input,
            output,
            total,
            reservation.OverageInputTokens,
            reservation.OverageOutputTokens,
            reservation.OverageTotalTokens);
        JobPostingParseDraftMetrics.BudgetReconciliationSettled.Add(1);
        activity?.SetTag("reconciliation.outcome", "settled");
        return new(reservation.Status, true);
    }

    private async Task<AiUsageSettlementResult> ChangeReservedStateAsync(
        Guid reservationId,
        AiUsageReservationStatus target,
        bool releaseCapacity,
        CancellationToken cancellationToken)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation identifier is required.", nameof(reservationId));
        }

        if (!_options.Enabled)
        {
            return new(target, false);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockReservationAsync(reservationId, transaction, cancellationToken);
        var reservation = await LoadReservationAsync(reservationId, cancellationToken);
        if (reservation.Status != AiUsageReservationStatus.Reserved)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(reservation.Status, false);
        }

        var now = GetUtcNow();
        if (releaseCapacity)
        {
            var ids = reservation.Periods.Select(item => item.AiUsageBudgetPeriodId).ToArray();
            await LockPeriodsAsync(ids, transaction, cancellationToken);
            var periods = await _dbContext.AiUsageBudgetPeriods
                .Where(item => ids.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            foreach (var link in reservation.Periods)
            {
                var period = periods[link.AiUsageBudgetPeriodId];
                period.ReservedRequests -= link.ReservedRequestCount;
                period.ReservedTokens -= link.ReservedTokenCount;
                period.UpdatedAt = now;
            }

            reservation.Status = AiUsageReservationStatus.Released;
            reservation.ReleasedAt = now;
        }
        else
        {
            ApplyReconciliationRequired(
                reservation,
                now,
                AiUsageReconciliationReason.UnknownProviderOutcome);
        }

        reservation.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (releaseCapacity)
        {
            JobPostingParseDraftMetrics.BudgetReleased.Add(1);
        }
        else
        {
            JobPostingParseDraftMetrics.BudgetReconciliationRequired.Add(1);
        }

        return new(reservation.Status, true);
    }

    private async Task<AiUsageReservation> LoadReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        await _dbContext.AiUsageReservations
            .Include(item => item.Periods)
            .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken)
        ?? throw new InvalidOperationException("The AI usage reservation was not found.");

    private async Task EnsurePeriodAsync(
        AiUsageBudgetScopeType scope,
        Guid? userId,
        AiUsageBudgetPeriodWindow window,
        long requestLimit,
        long tokenLimit,
        DateTimeOffset now,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            INSERT INTO "AiUsageBudgetPeriods"
                ("Id", "ScopeType", "UserId", "PeriodType", "PeriodStart", "PeriodEnd",
                 "RequestLimit", "TokenLimit", "ReservedRequests", "ReservedTokens",
                 "ConsumedRequests", "ConsumedInputTokens", "ConsumedOutputTokens",
                 "ConsumedTotalTokens", "CreatedAt", "UpdatedAt")
            VALUES
                (@id, @scopeType, @userId, @periodType, @periodStart, @periodEnd,
                 @requestLimit, @tokenLimit, 0, 0, 0, 0, 0, 0, @createdAt, NULL)
            ON CONFLICT DO NOTHING;
            """;
        AddParameter(command, "id", Guid.NewGuid());
        AddParameter(command, "scopeType", (int)scope);
        AddParameter(command, "userId", userId ?? (object)DBNull.Value);
        AddParameter(command, "periodType", (int)window.PeriodType);
        AddParameter(command, "periodStart", window.Start);
        AddParameter(command, "periodEnd", window.End);
        AddParameter(command, "requestLimit", requestLimit);
        AddParameter(command, "tokenLimit", tokenLimit);
        AddParameter(command, "createdAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<Guid>> FindPeriodIdsAsync(
        Guid userId,
        DateTimeOffset dailyStart,
        DateTimeOffset monthlyStart,
        CancellationToken cancellationToken) =>
        await _dbContext.AiUsageBudgetPeriods
            .AsNoTracking()
            .Where(item =>
                (item.ScopeType == AiUsageBudgetScopeType.System && item.UserId == null ||
                 item.ScopeType == AiUsageBudgetScopeType.User && item.UserId == userId) &&
                (item.PeriodStart == dailyStart || item.PeriodStart == monthlyStart))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

    private async Task LockReservationAsync(
        Guid reservationId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT \"Id\" FROM \"AiUsageReservations\" WHERE \"Id\" = @id FOR UPDATE;";
        AddParameter(command, "id", reservationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The AI usage reservation was not found.");
        }
    }

    private async Task LockPeriodsAsync(
        IReadOnlyCollection<Guid> ids,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT "Id"
            FROM "AiUsageBudgetPeriods"
            WHERE "Id" = ANY(@ids)
            ORDER BY "ScopeType" ASC, "UserId" ASC NULLS FIRST,
                     "PeriodType" ASC, "PeriodStart" ASC
            FOR UPDATE;
            """;
        command.Parameters.Add(new NpgsqlParameter<Guid[]>("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            TypedValue = ids.ToArray()
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private static void ApplyReconciliationRequired(
        AiUsageReservation reservation,
        DateTimeOffset now,
        AiUsageReconciliationReason reason)
    {
        reservation.Status = AiUsageReservationStatus.ReconciliationRequired;
        reservation.ReconciliationRequiredAt = now;
        reservation.ReconciliationReason = reason;
        reservation.ReconciliationLeaseOwner = null;
        reservation.ReconciliationLeaseExpiresAt = null;
        reservation.UpdatedAt = now;
    }

    private async Task ApplySettlementAsync(
        AiUsageReservation reservation,
        long input,
        long output,
        long total,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var periodIds = reservation.Periods
            .Select(item => item.AiUsageBudgetPeriodId)
            .ToArray();
        await LockPeriodsAsync(periodIds, transaction, cancellationToken);
        var periods = await _dbContext.AiUsageBudgetPeriods
            .Where(item => periodIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var now = GetUtcNow();
        foreach (var link in reservation.Periods)
        {
            var period = periods[link.AiUsageBudgetPeriodId];
            period.ReservedRequests -= link.ReservedRequestCount;
            period.ReservedTokens -= link.ReservedTokenCount;
            period.ConsumedRequests += 1;
            period.ConsumedInputTokens += input;
            period.ConsumedOutputTokens += output;
            period.ConsumedTotalTokens += total;
            period.UpdatedAt = now;
        }

        reservation.Status = AiUsageReservationStatus.Settled;
        reservation.SettledAt = now;
        reservation.ReleasedAt = null;
        reservation.ReconciliationRequiredAt = null;
        reservation.ReconciliationReason = null;
        reservation.ReconciliationLeaseOwner = null;
        reservation.ReconciliationLeaseExpiresAt = null;
        reservation.ActualInputTokens = input;
        reservation.ActualOutputTokens = output;
        reservation.ActualTotalTokens = total;
        var overage = AiUsageOverage.Calculate(
            reservation.ReservedInputTokens,
            reservation.ReservedOutputTokens,
            reservation.ReservedTotalTokens,
            input,
            output,
            total);
        reservation.OverageInputTokens = overage.InputTokens;
        reservation.OverageOutputTokens = overage.OutputTokens;
        reservation.OverageTotalTokens = overage.TotalTokens;
        reservation.UpdatedAt = now;
    }

    private static void RecordSettlementMetrics(
        long input,
        long output,
        long total,
        long overageInput,
        long overageOutput,
        long overageTotal)
    {
        JobPostingParseDraftMetrics.BudgetSettled.Add(1);
        JobPostingParseDraftMetrics.BudgetInputTokens.Add(input);
        JobPostingParseDraftMetrics.BudgetOutputTokens.Add(output);
        JobPostingParseDraftMetrics.BudgetTotalTokens.Add(total);
        if (overageInput > 0 || overageOutput > 0 || overageTotal > 0)
        {
            JobPostingParseDraftMetrics.BudgetReconciliationOverage.Add(1);
            if (overageInput > 0)
            {
                JobPostingParseDraftMetrics.BudgetOverageInputTokens.Add(overageInput);
            }

            if (overageOutput > 0)
            {
                JobPostingParseDraftMetrics.BudgetOverageOutputTokens.Add(overageOutput);
            }

            if (overageTotal > 0)
            {
                JobPostingParseDraftMetrics.BudgetOverageTotalTokens.Add(overageTotal);
            }
        }
    }

    private static string? NormalizeMetadata(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private DateTimeOffset GetUtcNow() =>
        _timeProvider.GetUtcNow().ToUniversalTime();

    private static void ValidateRequest(AiUsageReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId == Guid.Empty || request.DraftId == Guid.Empty || request.AttemptCount < 1)
        {
            throw new ArgumentException("A valid user, draft and attempt are required.", nameof(request));
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
