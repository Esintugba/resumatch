namespace Api.Entities;

/// <summary>
/// A single refresh token. Only its SHA-256 hash is stored. A consumed row is
/// kept while its session lives, so that a second use can be detected.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid SessionId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset? ConsumedAt { get; set; }

    public AuthSession Session { get; set; } = null!;
}
