namespace Api.Application.Auth;

/// <summary>
/// Reduces a User-Agent header to a coarse label such as "Firefox on Linux",
/// so a session list is recognizable without storing the raw header.
/// </summary>
public static class DeviceLabel
{
    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser =
            Has(userAgent, "Edg/") ? "Edge" :
            Has(userAgent, "OPR/") ? "Opera" :
            Has(userAgent, "Firefox/") ? "Firefox" :
            Has(userAgent, "Chrome/") ? "Chrome" :
            Has(userAgent, "Safari/") ? "Safari" :
            null;
        var platform =
            Has(userAgent, "Android") ? "Android" :
            Has(userAgent, "iPhone") || Has(userAgent, "iPad") ? "iOS" :
            Has(userAgent, "Windows") ? "Windows" :
            Has(userAgent, "Mac OS X") ? "macOS" :
            Has(userAgent, "Linux") ? "Linux" :
            null;

        return (browser, platform) switch
        {
            (not null, not null) => $"{browser} on {platform}",
            (not null, null) => browser,
            (null, not null) => platform,
            _ => "Unknown device"
        };
    }

    private static bool Has(string value, string marker) =>
        value.Contains(marker, StringComparison.Ordinal);
}
