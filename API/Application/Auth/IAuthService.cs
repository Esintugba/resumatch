using Api.DTOs.Auth;

namespace Api.Application.Auth;

/// <summary>
/// A completed sign-in or refresh. The refresh token is kept apart from the
/// response body, so it can only ever reach the client in a cookie.
/// </summary>
public sealed record AuthenticationResult(
    AuthResponse Response,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record RefreshOutcome(
    RefreshStatus Status,
    AuthenticationResult? Result = null);

public interface IAuthService
{
    Task<AuthenticationResult> RegisterAsync(
        RegisterRequest request,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<AuthenticationResult> LoginAsync(
        LoginRequest request,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<RefreshOutcome> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken);

    Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken);
}
