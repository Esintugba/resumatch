using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Authentication;

public sealed class RefreshTokenCookieOptions
{
    public const string SectionName = "RefreshTokenCookie";
    public const string RequiredPrefix = "__Host-";

    public string Name { get; init; } = "__Host-resumatch_refresh";
}

public sealed class RefreshTokenCookieOptionsValidator :
    IValidateOptions<RefreshTokenCookieOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        RefreshTokenCookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // __Host- makes the browser refuse the cookie unless it is Secure, has
        // Path=/ and no Domain, so no sibling subdomain can set or shadow it.
        return options.Name is { Length: > 7 and <= 64 } &&
            options.Name.StartsWith(RefreshTokenCookieOptions.RequiredPrefix, StringComparison.Ordinal) &&
            options.Name.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "RefreshTokenCookie:Name must start with '__Host-' and contain only letters, digits, '-' and '_' (at most 64 characters).");
    }
}

/// <summary>
/// The only place the refresh token cookie is written, read or removed, so
/// every Set-Cookie carries the same attributes.
/// </summary>
public sealed class RefreshTokenCookie
{
    private readonly string _name;

    public RefreshTokenCookie(IOptions<RefreshTokenCookieOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _name = options.Value.Name;
    }

    public string Name => _name;

    public string? Read(HttpRequest request) => request.Cookies[_name];

    public void Write(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        var options = CreateOptions();
        options.Expires = expiresAt;
        response.Cookies.Append(_name, token, options);
        response.Headers.CacheControl = "no-store";
    }

    public void Delete(HttpResponse response)
    {
        response.Cookies.Delete(_name, CreateOptions());
        response.Headers.CacheControl = "no-store";
    }

    // Browsers treat http://localhost as a secure context and accept Secure
    // cookies there, so the attributes need no relaxing for local development.
    private static CookieOptions CreateOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Domain = null,
        IsEssential = true
    };
}
