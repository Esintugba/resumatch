using System.Data;
using System.Data.Common;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Policies;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing.Protection;

public sealed class AiProviderCircuitBreaker : IAiProviderCircuitBreaker
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly string _providerScope;
    private readonly bool _enabled;
    private readonly int _threshold;
    private readonly TimeSpan _openDuration;
    private readonly int _halfOpenMaxProbes;
    private readonly TimeSpan _capacityRetryDelay;
    private readonly TimeSpan _probeLeaseDuration;

    public AiProviderCircuitBreaker(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<JobPostingParseDraftOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _providerScope = options.Value.DistributedProviderScope;
        _enabled = options.Value.CircuitBreakerEnabled;
        _threshold = options.Value.CircuitBreakerFailureThreshold;
        _openDuration = options.Value.CircuitBreakerOpenDuration;
        _halfOpenMaxProbes = options.Value.CircuitBreakerHalfOpenMaxProbes;
        _capacityRetryDelay = options.Value.ProviderCapacityRetryDelay;
        _probeLeaseDuration = options.Value.LeaseDuration;
    }

    public async Task<AiProviderCircuitPermitResult> TryAcquirePermissionAsync(
        CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return new(true, null, null, true);
        }

        var now = GetUtcNow();
        await using var transaction = await BeginLockedTransactionAsync(cancellationToken);
        var state = await LoadStateAsync(now, cancellationToken);
        if (state.Status == AiProviderCircuitStatus.Open)
        {
            if (state.OpenUntil > now)
            {
                var retryAt = state.OpenUntil;
                await transaction.CommitAsync(cancellationToken);
                JobPostingParseDraftMetrics.CircuitRejected.Add(1);
                return new(false, null, retryAt);
            }

            state.Status = AiProviderCircuitStatus.HalfOpen;
            state.OpenUntil = null;
            state.ActiveProbes = 0;
            state.Generation++;
        }

        if (state.Status == AiProviderCircuitStatus.HalfOpen &&
            await RefreshActiveProbesAsync(state, now, cancellationToken) >=
            _halfOpenMaxProbes)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            JobPostingParseDraftMetrics.CircuitRejected.Add(1);
            JobPostingParseDraftMetrics.CircuitProbeDenied.Add(1);
            return new(false, null, now.Add(_capacityRetryDelay));
        }

        state.NextSequence++;
        var isProbe = state.Status == AiProviderCircuitStatus.HalfOpen;
        AiProviderCircuitProbeLease? probeLease = null;
        if (isProbe)
        {
            probeLease = new AiProviderCircuitProbeLease
            {
                ProviderScope = _providerScope,
                Generation = state.Generation,
                Sequence = state.NextSequence,
                OwnerToken = Guid.NewGuid(),
                AcquiredAt = now,
                ExpiresAt = now.Add(_probeLeaseDuration),
                Status = AiProviderCircuitProbeStatus.Active
            };
            _dbContext.AiProviderCircuitProbeLeases.Add(probeLease);
            state.ActiveProbes++;
            JobPostingParseDraftMetrics.CircuitHalfOpenProbes.Add(1);
        }

        state.UpdatedAt = now;
        var token = new AiProviderCircuitPermitToken(
            state.Generation,
            state.NextSequence,
            isProbe,
            probeLease?.Id,
            probeLease?.OwnerToken,
            probeLease?.ExpiresAt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, token, null);
    }

    public Task<bool> RecordSuccessAsync(
        AiProviderCircuitPermitToken token,
        CancellationToken cancellationToken) =>
        CompleteAsync(token, success: true, qualifyingFailure: false, cancellationToken);

    public Task<bool> RecordFailureAsync(
        AiProviderCircuitPermitToken token,
        JobParseFailureClassification failure,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            token,
            success: false,
            qualifyingFailure: AiProviderCircuitFailurePolicy.IsQualifying(failure),
            cancellationToken);

    public Task<bool> ReleasePermissionAsync(
        AiProviderCircuitPermitToken token,
        CancellationToken cancellationToken) =>
        CompleteAsync(token, success: false, qualifyingFailure: false, cancellationToken);

    private async Task<bool> CompleteAsync(
        AiProviderCircuitPermitToken token,
        bool success,
        bool qualifyingFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!_enabled)
        {
            return false;
        }

        var now = GetUtcNow();
        await using var transaction = await BeginLockedTransactionAsync(cancellationToken);
        DetachTrackedState();
        var state = await _dbContext.AiProviderCircuitStates
            .SingleAsync(item => item.ProviderScope == _providerScope, cancellationToken);
        if (state.Generation != token.Generation)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (token.IsHalfOpenProbe)
        {
            var probeLease = await LoadValidProbeLeaseAsync(
                token, now, cancellationToken);
            if (state.Status != AiProviderCircuitStatus.HalfOpen ||
                state.ActiveProbes < 1 ||
                probeLease is null)
            {
                if (state.Status == AiProviderCircuitStatus.HalfOpen)
                {
                    await RefreshActiveProbesAsync(state, now, cancellationToken);
                    state.UpdatedAt = now;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            probeLease.Status = AiProviderCircuitProbeStatus.Completed;
            probeLease.CompletedAt = now;
            probeLease.UpdatedAt = now;

            if (success)
            {
                await ExpireOtherActiveProbesAsync(
                    state.Generation, probeLease.Id, now, cancellationToken);
                Close(state, now);
                JobPostingParseDraftMetrics.CircuitHalfOpenSuccess.Add(1);
                JobPostingParseDraftMetrics.CircuitClosed.Add(1);
            }
            else if (qualifyingFailure)
            {
                await ExpireOtherActiveProbesAsync(
                    state.Generation, probeLease.Id, now, cancellationToken);
                Open(state, now);
                JobPostingParseDraftMetrics.CircuitHalfOpenFailure.Add(1);
                JobPostingParseDraftMetrics.CircuitOpened.Add(1);
            }
            else
            {
                state.ActiveProbes--;
                state.UpdatedAt = now;
            }
        }
        else if (state.Status == AiProviderCircuitStatus.Closed)
        {
            if (qualifyingFailure)
            {
                state.ConsecutiveFailures++;
                state.LastFailureSequence = Math.Max(
                    state.LastFailureSequence, token.Sequence);
                if (state.ConsecutiveFailures >= _threshold)
                {
                    Open(state, now);
                    JobPostingParseDraftMetrics.CircuitOpened.Add(1);
                }
            }
            else if (success && token.Sequence > state.LastFailureSequence)
            {
                state.ConsecutiveFailures = 0;
            }

            state.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<AiProviderCircuitState> LoadStateAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "AiProviderCircuitStates"
                ("Id", "ProviderScope", "Status", "ConsecutiveFailures", "OpenUntil",
                 "Generation", "NextSequence", "LastFailureSequence", "ActiveProbes",
                 "CreatedAt", "UpdatedAt")
            VALUES ({Guid.NewGuid()}, {_providerScope}, 0, 0, NULL, 0, 0, 0, 0, {now}, NULL)
            ON CONFLICT ("ProviderScope") DO NOTHING;
            """, cancellationToken);
        await LockStateRowAsync(cancellationToken);
        DetachTrackedState();
        return await _dbContext.AiProviderCircuitStates
            .SingleAsync(item => item.ProviderScope == _providerScope, cancellationToken);
    }

    private async Task<int> RefreshActiveProbesAsync(
        AiProviderCircuitState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recovered = await _dbContext.AiProviderCircuitProbeLeases
            .Where(item =>
                item.ProviderScope == _providerScope &&
                item.Generation == state.Generation &&
                item.Status == AiProviderCircuitProbeStatus.Active &&
                item.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AiProviderCircuitProbeStatus.Expired)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (recovered > 0)
        {
            JobPostingParseDraftMetrics.CircuitProbeRecovered.Add(recovered);
        }

        state.ActiveProbes = await _dbContext.AiProviderCircuitProbeLeases
            .CountAsync(item =>
                item.ProviderScope == _providerScope &&
                item.Generation == state.Generation &&
                item.Status == AiProviderCircuitProbeStatus.Active &&
                item.ExpiresAt > now,
                cancellationToken);
        return state.ActiveProbes;
    }

    private async Task<AiProviderCircuitProbeLease?> LoadValidProbeLeaseAsync(
        AiProviderCircuitPermitToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (token.ProbeLeaseId is not { } leaseId ||
            token.ProbeOwnerToken is not { } ownerToken ||
            token.ProbeExpiresAt is not { } expiresAt ||
            expiresAt <= now)
        {
            await ExpireProbeIfPresentAsync(token, now, cancellationToken);
            return null;
        }

        return await _dbContext.AiProviderCircuitProbeLeases
            .SingleOrDefaultAsync(item =>
                item.Id == leaseId &&
                item.ProviderScope == _providerScope &&
                item.Generation == token.Generation &&
                item.Sequence == token.Sequence &&
                item.OwnerToken == ownerToken &&
                item.Status == AiProviderCircuitProbeStatus.Active &&
                item.CompletedAt == null &&
                item.ExpiresAt > now,
                cancellationToken);
    }

    private async Task ExpireProbeIfPresentAsync(
        AiProviderCircuitPermitToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (token.ProbeLeaseId is not { } leaseId ||
            token.ProbeOwnerToken is not { } ownerToken)
        {
            return;
        }

        await _dbContext.AiProviderCircuitProbeLeases
            .Where(item =>
                item.Id == leaseId &&
                item.ProviderScope == _providerScope &&
                item.Generation == token.Generation &&
                item.Sequence == token.Sequence &&
                item.OwnerToken == ownerToken &&
                item.Status == AiProviderCircuitProbeStatus.Active &&
                item.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AiProviderCircuitProbeStatus.Expired)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    private Task<int> ExpireOtherActiveProbesAsync(
        long generation,
        Guid completedLeaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _dbContext.AiProviderCircuitProbeLeases
            .Where(item =>
                item.ProviderScope == _providerScope &&
                item.Generation == generation &&
                item.Id != completedLeaseId &&
                item.Status == AiProviderCircuitProbeStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AiProviderCircuitProbeStatus.Expired)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>
        BeginLockedTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT pg_advisory_xact_lock(hashtextextended(@scope, 141010));";
        AddParameter(command, "scope", _providerScope);
        await command.ExecuteScalarAsync(cancellationToken);
        return transaction;
    }

    private async Task LockStateRowAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AiProviderCircuitStates\" WHERE \"ProviderScope\" = {_providerScope} FOR UPDATE;",
            cancellationToken);
    }

    private void Open(AiProviderCircuitState state, DateTimeOffset now)
    {
        state.Status = AiProviderCircuitStatus.Open;
        state.OpenUntil = now.Add(_openDuration);
        state.ActiveProbes = 0;
        state.Generation++;
        state.UpdatedAt = now;
    }

    private static void Close(AiProviderCircuitState state, DateTimeOffset now)
    {
        state.Status = AiProviderCircuitStatus.Closed;
        state.OpenUntil = null;
        state.ActiveProbes = 0;
        state.ConsecutiveFailures = 0;
        state.LastFailureSequence = state.NextSequence;
        state.Generation++;
        state.UpdatedAt = now;
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow().ToUniversalTime();

    private void DetachTrackedState()
    {
        foreach (var entry in _dbContext.ChangeTracker
                     .Entries<AiProviderCircuitState>().ToArray())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in _dbContext.ChangeTracker
                     .Entries<AiProviderCircuitProbeLease>().ToArray())
        {
            entry.State = EntityState.Detached;
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
