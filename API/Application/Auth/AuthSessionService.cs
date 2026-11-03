using System.Data;
using System.Data.Common;
using Api.Data;
using Api.DTOs.Auth;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Application.Auth;

/// <summary>
/// Every operation that reads and then changes session state runs in one
/// database transaction and takes row locks with FOR UPDATE. Locks end with
/// the transaction, so this is correct behind a transaction-mode pooler such
/// as Neon's PgBouncer: nothing relies on a session-level lock or setting.
/// </summary>
public sealed class AuthSessionService : IAuthSessionService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly AuthSessionOptions _options;
    private readonly ILogger<AuthSessionService> _logger;

    public AuthSessionService(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<AuthSessionOptions> options,
        ILogger<AuthSessionService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? NullLogger<AuthSessionService>.Instance;
    }

    public async Task<AuthSession> StartAsync(
        User user,
        string? deviceLabel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = Now();

        await using var transaction = await BeginAsync(cancellationToken);
        // Serializes concurrent sign-ins of one user, so the limit holds.
        await LockAsync(
            transaction,
            """SELECT 1 FROM "Users" WHERE "Id" = @id FOR UPDATE;""",
            user.Id,
            cancellationToken);

        var active = await ActiveSessions(user.Id, now)
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .ToListAsync(cancellationToken);
        foreach (var oldest in active.Take(
            Math.Max(0, active.Count - _options.MaximumActiveSessionsPerUser + 1)))
        {
            Revoke(oldest, AuthSessionRevocationReason.SessionLimitReached, now);
        }

        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastUsedAt = now,
            AbsoluteExpiresAt = now + _options.RefreshTokenAbsoluteLifetime,
            IdleExpiresAt = now + _options.RefreshTokenIdleLifetime,
            DeviceLabel = deviceLabel is { Length: > 100 } ? deviceLabel[..100] : deviceLabel
        };
        _dbContext.AuthSessions.Add(session);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return session;
    }

    public async Task<string> IssueRefreshTokenAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = await BeginAsync(cancellationToken);
        await LockAsync(
            transaction,
            """SELECT 1 FROM "AuthSessions" WHERE "Id" = @id FOR UPDATE;""",
            sessionId,
            cancellationToken);

        var session = await _dbContext.AuthSessions
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null || !IsUsable(session, now))
        {
            throw new InvalidOperationException("The session is not active.");
        }

        var token = AddToken(session, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (!RefreshTokenCodec.TryHash(refreshToken, out var hash))
        {
            return new RefreshResult(RefreshStatus.Invalid);
        }

        var now = Now();
        await using var transaction = await BeginAsync(cancellationToken);

        // Lock order is always token, then session. Revocation locks only the
        // session, so the two paths cannot deadlock.
        var tokenId = await LockTokenAsync(transaction, hash, cancellationToken);
        if (tokenId is null)
        {
            return new RefreshResult(RefreshStatus.Invalid);
        }

        var token = await _dbContext.RefreshTokens
            .SingleAsync(item => item.Id == tokenId.Value, cancellationToken);
        await LockAsync(
            transaction,
            """SELECT 1 FROM "AuthSessions" WHERE "Id" = @id FOR UPDATE;""",
            token.SessionId,
            cancellationToken);
        var session = await _dbContext.AuthSessions
            .Include(item => item.User)
            .SingleAsync(item => item.Id == token.SessionId, cancellationToken);

        if (!IsUsable(session, now))
        {
            return new RefreshResult(RefreshStatus.Invalid);
        }

        if (token.ConsumedAt is { } consumedAt)
        {
            if (now - consumedAt <= _options.RotationGracePeriod)
            {
                return new RefreshResult(RefreshStatus.Superseded);
            }

            Revoke(session, AuthSessionRevocationReason.RefreshTokenReused, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogWarning(
                "A consumed refresh token was used again; the session was revoked. SessionId={SessionId}",
                session.Id);
            return new RefreshResult(RefreshStatus.Reused);
        }

        token.ConsumedAt = now;
        session.LastUsedAt = now;
        session.IdleExpiresAt = Min(
            now + _options.RefreshTokenIdleLifetime,
            session.AbsoluteExpiresAt);
        session.UpdatedAt = now;
        var next = AddToken(session, now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshResult(RefreshStatus.Succeeded, next, session);
    }

    public async Task<IReadOnlyList<AuthSessionResponse>> ListAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = Now();
        return await ActiveSessions(userId, now)
            .AsNoTracking()
            .OrderByDescending(session => session.LastUsedAt)
            .ThenBy(session => session.Id)
            .Select(session => new AuthSessionResponse
            {
                Id = session.Id,
                DeviceLabel = session.DeviceLabel,
                CreatedAt = session.CreatedAt,
                LastUsedAt = session.LastUsedAt,
                IsCurrent = session.Id == currentSessionId
            })
            .ToListAsync(cancellationToken);
    }

    public Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken) =>
        RevokeSessionAsync(sessionId, userId, reason, cancellationToken);

    public async Task<bool> RevokeByRefreshTokenAsync(
        string? refreshToken,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        if (!RefreshTokenCodec.TryHash(refreshToken, out var hash))
        {
            return false;
        }

        // Any token of the session, even an already rotated one, signs it out:
        // whoever holds it held the session, and reuse would revoke it anyway.
        var sessionId = await _dbContext.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => (Guid?)token.SessionId)
            .SingleOrDefaultAsync(cancellationToken);
        return sessionId is not null &&
            await RevokeSessionAsync(sessionId.Value, userId: null, reason, cancellationToken);
    }

    private async Task<bool> RevokeSessionAsync(
        Guid sessionId,
        Guid? userId,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = await BeginAsync(cancellationToken);
        await LockAsync(
            transaction,
            """SELECT 1 FROM "AuthSessions" WHERE "Id" = @id FOR UPDATE;""",
            sessionId,
            cancellationToken);

        var session = await _dbContext.AuthSessions
            .SingleOrDefaultAsync(
                item => item.Id == sessionId && (userId == null || item.UserId == userId),
                cancellationToken);
        if (session is null || session.RevokedAt is not null)
        {
            return false;
        }

        Revoke(session, reason, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAllAsync(
        Guid userId,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = await BeginAsync(cancellationToken);
        await LockAsync(
            transaction,
            """SELECT 1 FROM "Users" WHERE "Id" = @id FOR UPDATE;""",
            userId,
            cancellationToken);

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");
        user.SecurityStamp = Guid.NewGuid();
        user.UpdatedAt = now;

        var sessions = await _dbContext.AuthSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            Revoke(session, reason, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        // Kept for PurgeRetention after they stop being usable, so reuse of a
        // just-expired token is still recognized and can be investigated.
        var cutoff = Now() - _options.PurgeRetention;
        return await _dbContext.AuthSessions
            .Where(session =>
                session.RevokedAt < cutoff ||
                session.AbsoluteExpiresAt < cutoff ||
                session.IdleExpiresAt < cutoff)
            .OrderBy(session => session.Id)
            .Take(_options.PurgeBatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<AuthSession> ActiveSessions(Guid userId, DateTimeOffset now) =>
        _dbContext.AuthSessions.Where(session =>
            session.UserId == userId &&
            session.RevokedAt == null &&
            session.AbsoluteExpiresAt > now &&
            session.IdleExpiresAt > now);

    private static bool IsUsable(AuthSession session, DateTimeOffset now) =>
        session.RevokedAt is null &&
        session.AbsoluteExpiresAt > now &&
        session.IdleExpiresAt > now &&
        session.User.IsActive;

    private string AddToken(AuthSession session, DateTimeOffset now)
    {
        var (token, hash) = RefreshTokenCodec.Create();
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TokenHash = hash,
            CreatedAt = now
        });
        return token;
    }

    private static void Revoke(
        AuthSession session,
        AuthSessionRevocationReason reason,
        DateTimeOffset now)
    {
        session.RevokedAt = now;
        session.RevokedReason = reason;
        session.UpdatedAt = now;
    }

    private DateTimeOffset Now() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first < second ? first : second;

    private Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    private async Task LockAsync(
        IDbContextTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql);
        AddParameter(command, "id", id);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<Guid?> LockTokenAsync(
        IDbContextTransaction transaction,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            transaction,
            """SELECT "Id" FROM "RefreshTokens" WHERE "TokenHash" = @hash FOR UPDATE;""");
        AddParameter(command, "hash", hash);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private DbCommand CreateCommand(IDbContextTransaction transaction, string sql)
    {
        var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
