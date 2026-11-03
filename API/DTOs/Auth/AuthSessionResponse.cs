namespace Api.DTOs.Auth;

public sealed class AuthSessionResponse
{
    public Guid Id { get; init; }
    public string? DeviceLabel { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastUsedAt { get; init; }
    public bool IsCurrent { get; init; }
}
