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
            if (!IsValidOrigin(origin))
            {
                failures.Add(
                    "Each CORS origin must be an absolute HTTP or HTTPS origin without wildcard, credentials, query, or fragment.");
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

        if (options.TrustedProxies.Any(value =>
                !IPAddress.TryParse(value, out _)))
        {
            failures.Add("Every TrustedProxies entry must be an IP address.");
        }

        if (options.TrustedNetworks.Any(value =>
                !IPNetwork.TryParse(value, out _)))
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

    private static bool IsValidOrigin(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
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
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return false;
        }

        return true;
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
