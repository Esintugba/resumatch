namespace Api.DTOs.Auth;

public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public CurrentUserResponse User { get; init; } = null!;
}
