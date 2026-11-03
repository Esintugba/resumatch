namespace Api.Infrastructure.Authentication;

/// <summary>
/// Custom JWT claims. Short standard names such as "sid" are remapped to long
/// claim types on read, so these deliberately avoid that table.
/// </summary>
public static class AuthClaimTypes
{
    public const string SessionId = "session";
    public const string SecurityStamp = "stamp";
}
