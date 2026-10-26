using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

public sealed class HttpSecurityOptions
{
    public const string SectionName = "HttpSecurity";

    public string[] AllowedOrigins { get; init; } = [];
    public bool AllowCredentials { get; init; }
    public string[] TrustedProxies { get; init; } = [];
    public string[] TrustedNetworks { get; init; } = [];
    public int ForwardLimit { get; init; } = 1;
    public bool HstsEnabled { get; init; } = true;
    public TimeSpan HstsMaxAge { get; init; } = TimeSpan.FromDays(30);
    public bool HstsIncludeSubDomains { get; init; }
    public bool HstsPreload { get; init; }
    public bool ExposeOpenApi { get; init; }
}

public sealed class HttpSecurityOptionsValidator :
    IValidateOptions<HttpSecurityOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        HttpSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.AllowedOrigins.Length > 50)
        {
            failures.Add("AllowedOrigins cannot contain more than 50 entries.");
        }

        foreach (var origin in options.AllowedOrigins)
        {
            if (!IsValidOrigin(origin, out var uri))
            {
                failures.Add(
                    "Each CORS origin must be an absolute HTTP or HTTPS origin without wildcard, credentials, query, or fragment.");
                break;
            }

            // CORS compares origins literally with the browser Origin header,
            // which never has a path, so "https://host/" would never match.
            if (HasPath(origin, uri))
            {
                failures.Add(
                    $"Origin must not contain a path or trailing slash: '{origin}'. " +
                    $"Use '{uri.GetLeftPart(UriPartial.Authority)}'.");
                break;
            }
        }

        if (options.AllowCredentials && options.AllowedOrigins.Length == 0)
        {
            failures.Add(
                "CORS credentials require at least one explicitly allowed origin.");
        }

        if (options.TrustedProxies.Length > 50 ||
            options.TrustedNetworks.Length > 50)
        {
            failures.Add(
                "Trusted proxy and network lists cannot exceed 50 entries each.");
        }

        if (options.TrustedProxies.Any(value => !IsStrictIpAddress(value)))
        {
            failures.Add("Every TrustedProxies entry must be an IP address.");
        }

        if (options.TrustedNetworks.Any(value => !IsStrictIpNetwork(value)))
        {
            failures.Add("Every TrustedNetworks entry must use valid CIDR notation.");
        }

        if (options.ForwardLimit is < 1 or > 5)
        {
            failures.Add("ForwardLimit must be between 1 and 5.");
        }

        if (options.HstsMaxAge < TimeSpan.FromDays(1) ||
            options.HstsMaxAge > TimeSpan.FromDays(365))
        {
            failures.Add("HstsMaxAge must be between one day and one year.");
        }

        if (options.HstsPreload &&
            (!options.HstsIncludeSubDomains ||
             options.HstsMaxAge < TimeSpan.FromDays(365)))
        {
            failures.Add(
                "HSTS preload requires includeSubDomains and a one-year max age.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidOrigin(
        string value,
        [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*') ||
            !value.Contains("://", StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out uri) ||
            !(string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return true;
    }

    // IPAddress.TryParse also accepts legacy IPv4 forms such as "10.0.0"
    // (read as 10.0.0.0), "10" or octal/hex parts, so IPv4 must be an explicit
    // dotted quad and IPv6 must use colon notation.
    private static bool IsStrictIpAddress(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            !IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsDottedQuad(value),
            System.Net.Sockets.AddressFamily.InterNetworkV6 =>
                value.Contains(':', StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool IsStrictIpNetwork(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 ||
            separator != value.LastIndexOf('/') ||
            !IsStrictIpAddress(value[..separator]))
        {
            return false;
        }

        var prefix = value[(separator + 1)..];
        return prefix.Length is > 0 and <= 3 &&
            prefix.All(char.IsAsciiDigit) &&
            (prefix.Length == 1 || prefix[0] != '0') &&
            IPNetwork.TryParse(value, out _);
    }

    private static bool IsDottedQuad(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part =>
            part.Length is > 0 and <= 3 &&
            part.All(char.IsAsciiDigit) &&
            (part.Length == 1 || part[0] != '0') &&
            int.Parse(part, System.Globalization.CultureInfo.InvariantCulture) <= 255);
    }

    private static bool HasPath(string value, Uri uri)
    {
        var authorityStart =
            value.IndexOf("://", StringComparison.Ordinal) + "://".Length;

        // Uri normalizes "/" and dot segments away, so inspect the raw value too.
        return value.IndexOfAny(['/', '\\'], authorityStart) >= 0 ||
            uri.AbsolutePath != "/";
    }
}

public static class ProductionSecurityPolicy
{
    public static IReadOnlyList<string> Validate(
        bool isProduction,
        string? allowedHosts,
        HttpSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!isProduction)
        {
            return [];
        }

        var failures = new List<string>();
        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => host == "*"))
        {
            failures.Add(
                "Production AllowedHosts must contain explicit trusted hosts and cannot use wildcard.");
        }

        if (!options.HstsEnabled)
        {
            failures.Add("HSTS must be enabled in production.");
        }

        return failures;
    }
}
