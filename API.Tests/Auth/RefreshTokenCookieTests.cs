using Api.Infrastructure.Authentication;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace API.Tests.Auth;

public sealed class RefreshTokenCookieTests
{
    private static readonly RefreshTokenCookie Cookie =
        new(Options.Create(new RefreshTokenCookieOptions()));

    [Fact]
    public void Write_SetsHostOnlyStrictSecureHttpOnlyCookie()
    {
        var context = new DefaultHttpContext();
        var expiresAt = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        Cookie.Write(context.Response, "token-value", expiresAt);

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.Single());
        Assert.Equal("__Host-resumatch_refresh", cookie.Name.Value);
        Assert.Equal("token-value", cookie.Value.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.Null(cookie.Domain.Value);
        Assert.Equal(expiresAt, cookie.Expires);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void Delete_ExpiresTheCookieWithTheSameAttributes()
    {
        var context = new DefaultHttpContext();

        Cookie.Delete(context.Response);

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.Single());
        Assert.Equal("__Host-resumatch_refresh", cookie.Name.Value);
        Assert.Equal(string.Empty, cookie.Value.Value);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.Null(cookie.Domain.Value);
    }

    [Theory]
    [InlineData("__Host-resumatch_refresh", true)]
    [InlineData("__Host-dev-refresh", true)]
    [InlineData("__Secure-resumatch_refresh", false)]
    [InlineData("resumatch_refresh", false)]
    [InlineData("__Host-", false)]
    [InlineData("__host-resumatch", false)]
    [InlineData("__Host-name;Domain=evil", false)]
    [InlineData("__Host-name with space", false)]
    public void CookieName_MustUseTheHostPrefix(string name, bool valid)
    {
        var result = new RefreshTokenCookieOptionsValidator().Validate(
            null,
            new RefreshTokenCookieOptions { Name = name });

        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData(0, 60, false)]
    [InlineData(101, 60, false)]
    [InlineData(30, 0, false)]
    [InlineData(30, 7200, false)]
    [InlineData(30, 60, true)]
    public void RefreshAndLogoutLimits_AreValidated(int permitLimit, int windowSeconds, bool valid)
    {
        var window = TimeSpan.FromSeconds(windowSeconds);
        var validator = new AuthenticationRateLimitOptionsValidator();

        var refresh = validator.Validate(null, new AuthenticationRateLimitOptions
        {
            RefreshPermitLimit = permitLimit,
            RefreshWindow = window
        });
        var logout = validator.Validate(null, new AuthenticationRateLimitOptions
        {
            LogoutPermitLimit = permitLimit,
            LogoutWindow = window
        });

        Assert.Equal(valid, refresh.Succeeded);
        Assert.Equal(valid, logout.Succeeded);
    }
}
