using Api.Entities.Enums;

namespace Api.Entities;

/// <summary>
/// One signed-in device. Its refresh tokens rotate; the session holds the
/// lifetime and the revocation state shared by all of them.
/// </summary>
public class AuthSession : BaseEntity
{
    public Guid UserId { get; set; }

    // Updated only when a refresh token rotates, never on ordinary requests.
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public AuthSessionRevocationReason? RevokedReason { get; set; }

    // A coarse browser and platform label such as "Chrome on Windows". No IP address.
    public string? DeviceLabel { get; set; }

    public User User { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
