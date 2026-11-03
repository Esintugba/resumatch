using Api.DTOs.Auth;

namespace Api.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(
        LoginRequest request,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken);
}
