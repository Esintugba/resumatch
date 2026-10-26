namespace Api.DTOs.Auth;

public sealed class CurrentUserResponse
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? ProfileImageUrl { get; init; }
    public bool IsEmailVerified { get; init; }
}
