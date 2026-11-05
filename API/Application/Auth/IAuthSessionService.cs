using Api.DTOs.Auth;
using Api.Entities;
using Api.Entities.Enums;

namespace Api.Application.Auth;

public enum RefreshStatus
{
    Succeeded,
    Invalid,
    Superseded,
    Reused
}

public sealed record RefreshResult(
    RefreshStatus Status,
    string? RefreshToken = null,
    AuthSession? Session = null);

public interface IAuthSessionService
{
    /// <summary>Opens a session for a user who has just proven their identity.</summary>
    Task<AuthSession> StartAsync(
        User user,
        string? deviceLabel,
        CancellationToken cancellationToken);

    /// <summary>Creates the first refresh token of an active session.</summary>
    Task<string> IssueRefreshTokenAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<RefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthSessionResponse>> ListAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the session a refresh token belongs to. False when the token is
    /// unknown or its session is already revoked.
    /// </summary>
    Task<bool> RevokeByRefreshTokenAsync(
        string? refreshToken,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every session and changes the security stamp, so no access token
    /// issued before stays valid.
    /// </summary>
    Task RevokeAllAsync(
        Guid userId,
        AuthSessionRevocationReason reason,
        CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
