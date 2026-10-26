using System.Data;
using System.Data.Common;
using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing.Protection;

public sealed class AiProviderConcurrencyService : IAiProviderConcurrencyService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly string _providerScope;
    private readonly int _limit;

    public AiProviderConcurrencyService(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<JobPostingParseDraftOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _providerScope = options.Value.DistributedProviderScope;
        _limit = options.Value.DistributedMaxConcurrentRequests;
    }

    public async Task<AiProviderConcurrencyAcquireResult> TryAcquireAsync(
        Guid correlationId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var now = GetUtcNow();
        if (correlationId == Guid.Empty || expiresAt.Offset != TimeSpan.Zero || expiresAt <= now)
        {
            throw new ArgumentException("A valid correlation and future UTC expiry are required.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireProviderLockAsync(transaction, cancellationToken);
        var reclaimed = await _dbContext.AiProviderConcurrencyLeases
            .Where(item => item.ProviderScope == _providerScope &&
                item.Status == AiProviderConcurrencyLeaseStatus.Active &&
                item.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AiProviderConcurrencyLeaseStatus.Expired)
                .SetProperty(item => item.ReleasedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (reclaimed > 0)
        {
            JobPostingParseDraftMetrics.DistributedSlotsReclaimed.Add(reclaimed);
        }

        var active = await _dbContext.AiProviderConcurrencyLeases
            .CountAsync(item => item.ProviderScope == _providerScope &&
                item.Status == AiProviderConcurrencyLeaseStatus.Active &&
                item.ExpiresAt > now, cancellationToken);
        if (active >= _limit)
        {
            var retryAt = await _dbContext.AiProviderConcurrencyLeases
                .Where(item => item.ProviderScope == _providerScope &&
                    item.Status == AiProviderConcurrencyLeaseStatus.Active &&
                    item.ExpiresAt > now)
                .MinAsync(item => item.ExpiresAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            JobPostingParseDraftMetrics.DistributedSlotsDenied.Add(1);
            return new(false, null, retryAt);
        }

        var ownerToken = Guid.NewGuid();
        var lease = new AiProviderConcurrencyLease
        {
            ProviderScope = _providerScope,
            OwnerToken = ownerToken,
            CorrelationId = correlationId,
            AcquiredAt = now,
            ExpiresAt = expiresAt,
            Status = AiProviderConcurrencyLeaseStatus.Active,
            CreatedAt = now
        };
        _dbContext.AiProviderConcurrencyLeases.Add(lease);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        JobPostingParseDraftMetrics.DistributedSlotsAcquired.Add(1);
        return new(true, new(lease.Id, ownerToken, expiresAt), null);
    }

    public async Task<bool> ReleaseAsync(
        AiProviderConcurrencyLeaseToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var now = GetUtcNow();
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireProviderLockAsync(transaction, cancellationToken);
        var changed = await _dbContext.AiProviderConcurrencyLeases
            .Where(item => item.Id == token.LeaseId &&
                item.OwnerToken == token.OwnerToken &&
                item.Status == AiProviderConcurrencyLeaseStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AiProviderConcurrencyLeaseStatus.Released)
                .SetProperty(item => item.ReleasedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private async Task AcquireProviderLockAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT pg_advisory_xact_lock(hashtextextended(@scope, 141009));";
        AddParameter(command, "scope", _providerScope);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
