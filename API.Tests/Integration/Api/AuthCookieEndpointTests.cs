using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Application.Common.Exceptions;
using Api.DTOs.Auth;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace API.Tests.Integration.Api;

/// <summary>
/// Drives the cookie flow the way a browser on the allowed origin would. The
/// client never stores cookies itself, so each test states which cookie it sends.
/// </summary>
public sealed class AuthCookieEndpointTests : AnalysisApiTestBase
{
    private const string Origin = "https://app.example.com";
    private const string CookieName = "__Host-resumatch_refresh";
    private const string CsrfHeader = "X-ResuMatch-CSRF";
    private const string Password = "Correct-Horse-Battery-1";

    public AuthCookieEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected override ResuMatchWebApplicationFactory CreateApplication() =>
        new(
            Fixture,
            new Dictionary<string, string?>
            {
                ["HttpSecurity:AllowedOrigins:0"] = Origin,
                ["HttpSecurity:AllowCredentials"] = "true",
                ["AuthenticationRateLimits:RefreshPermitLimit"] = "100",
                ["AuthenticationRateLimits:LogoutPermitLimit"] = "100"
            });

    [Fact]
    public async Task Login_SetsTheRefreshCookieAndKeepsItOutOfTheBody()
    {
        using var cancellation = CreateCancellation();
        await RegisterAsync("cookie@example.com", cancellation.Token);

        using var response = await PostJsonAsync(
            "/api/auth/login",
            new { email = "cookie@example.com", password = Password },
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = RefreshCookie(response);
        Assert.Equal(43, cookie.Value.Length);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.Null(cookie.Domain.Value);
        Assert.InRange(
            cookie.Expires!.Value,
            DateTimeOffset.UtcNow.AddDays(13),
            DateTimeOffset.UtcNow.AddDays(15));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(Origin, Single(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Single(response, "Access-Control-Allow-Credentials"));

        var body = await response.Content.ReadAsStringAsync(cancellation.Token);
        Assert.DoesNotContain(cookie.Value.Value!, body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            ["accessToken", "expiresAt", "user"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task Register_SetsTheRefreshCookie()
    {
        using var cancellation = CreateCancellation();

        var (_, cookie) = await RegisterAsync("new@example.com", cancellation.Token);

        Assert.Equal(43, cookie.Length);
    }

    [Fact]
    public async Task AccessTokens_LastFifteenMinutes()
    {
        using var cancellation = CreateCancellation();

        var (auth, _) = await RegisterAsync("lifetime@example.com", cancellation.Token);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
    }

    [Fact]
    public async Task Refresh_RotatesTheCookieWithinTheSameSession()
    {
        using var cancellation = CreateCancellation();
        var (auth, first) = await RegisterAsync("rotate@example.com", cancellation.Token);

        using var firstRefresh = await RefreshAsync(first, cancellation.Token);
        var second = RefreshCookie(firstRefresh).Value.Value!;
        var refreshed = await ReadAuthAsync(firstRefresh, cancellation.Token);
        using var secondRefresh = await RefreshAsync(second, cancellation.Token);
        var third = RefreshCookie(secondRefresh).Value.Value!;

        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);
        Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
        Assert.Equal(SessionId(auth.AccessToken), SessionId(refreshed.AccessToken));
        Assert.Contains("no-store", firstRefresh.Headers.CacheControl!.ToString());
        using var client = Bearer(refreshed.AccessToken);
        using var me = await client.GetAsync("/api/auth/me", cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    public static TheoryData<string?, string?, string?> RejectedRequests => new()
    {
        // header, Origin, Referer
        { "1", null, null },
        { "1", "https://evil.example.com", null },
        { "1", "null", null },
        { "1", "http://app.example.com", null },
        { "1", null, "https://evil.example.com/page" },
        { "1", "https://evil.example.com", Origin + "/page" },
        { null, Origin, null },
        { "0", Origin, null },
        { "true", Origin, null }
    };

    [Theory]
    [MemberData(nameof(RejectedRequests))]
    public async Task Refresh_FailingCsrfChecks_IsRejectedAndLeavesTheTokenUsable(
        string? header,
        string? origin,
        string? referer)
    {
        using var cancellation = CreateCancellation();
        var (_, cookie) = await RegisterAsync("csrf@example.com", cancellation.Token);

        using var rejected = await SendCookieRequestAsync(
            "/api/auth/refresh",
            cookie,
            header,
            origin,
            referer,
            cancellation.Token);

        await AssertProblemAsync(
            rejected,
            HttpStatusCode.Forbidden,
            ApiErrorCodes.CsrfRejected,
            cancellation.Token);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        using var allowed = await RefreshAsync(cookie, cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RejectedRequests))]
    public async Task Logout_FailingCsrfChecks_IsRejectedAndKeepsTheSession(
        string? header,
        string? origin,
        string? referer)
    {
        using var cancellation = CreateCancellation();
        var (_, cookie) = await RegisterAsync("logout-csrf@example.com", cancellation.Token);

        using var rejected = await SendCookieRequestAsync(
            "/api/auth/logout",
            cookie,
            header,
            origin,
            referer,
            cancellation.Token);

        await AssertProblemAsync(
            rejected,
            HttpStatusCode.Forbidden,
            ApiErrorCodes.CsrfRejected,
            cancellation.Token);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        await using var dbContext = CreateDbContext();
        Assert.Equal(0, await dbContext.AuthSessions.CountAsync(
            session => session.RevokedAt != null,
            cancellation.Token));
    }

    [Fact]
    public async Task Refresh_AcceptsTheRefererWhenOriginIsAbsent()
    {
        using var cancellation = CreateCancellation();
        var (_, cookie) = await RegisterAsync("referer@example.com", cancellation.Token);

        using var response = await SendCookieRequestAsync(
            "/api/auth/refresh",
            cookie,
            "1",
            origin: null,
            referer: Origin + "/resumes",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_WithoutAValidCookie_Returns401AndClearsTheCookie(string? cookie)
    {
        using var cancellation = CreateCancellation();

        using var response = await RefreshAsync(cookie, cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            ApiErrorCodes.Unauthorized,
            cancellation.Token);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task Logout_RevokesTheSessionAndClearsTheCookie()
    {
        using var cancellation = CreateCancellation();
        var (auth, cookie) = await RegisterAsync("logout@example.com", cancellation.Token);

        using var logout = await SendCookieRequestAsync(
            "/api/auth/logout",
            cookie,
            "1",
            Origin,
            referer: null,
            cancellation.Token);
        using var refresh = await RefreshAsync(cookie, cancellation.Token);
        using var client = Bearer(auth.AccessToken);
        using var me = await client.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        AssertCookieCleared(logout);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        await using var dbContext = CreateDbContext();
        var session = await dbContext.AuthSessions.SingleAsync(cancellation.Token);
        Assert.Equal(AuthSessionRevocationReason.SignedOut, session.RevokedReason);
    }

    [Fact]
    public async Task Logout_WithARotatedToken_StillSignsTheSessionOut()
    {
        using var cancellation = CreateCancellation();
        var (_, first) = await RegisterAsync("rotated-logout@example.com", cancellation.Token);
        using var refreshed = await RefreshAsync(first, cancellation.Token);
        var second = RefreshCookie(refreshed).Value.Value!;

        using var logout = await SendCookieRequestAsync(
            "/api/auth/logout", first, "1", Origin, referer: null, cancellation.Token);
        using var refresh = await RefreshAsync(second, cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-token")]
    public async Task Logout_WithoutAKnownCookie_StillReturns204(string? cookie)
    {
        using var cancellation = CreateCancellation();

        using var response = await SendCookieRequestAsync(
            "/api/auth/logout", cookie, "1", Origin, referer: null, cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task TwoTabsRefreshingAtOnce_GetOneWinnerAndKeepTheSession()
    {
        using var cancellation = CreateCancellation();
        var (_, cookie) = await RegisterAsync("tabs@example.com", cancellation.Token);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => RefreshAsync(cookie, cancellation.Token)));
        try
        {
            var winner = Assert.Single(responses, item => item.StatusCode == HttpStatusCode.OK);
            foreach (var loser in responses.Where(item => item != winner))
            {
                await AssertProblemAsync(
                    loser,
                    HttpStatusCode.Conflict,
                    ApiErrorCodes.RefreshTokenSuperseded,
                    cancellation.Token);

                // Clearing the cookie here would sign the winning tab out.
                Assert.False(loser.Headers.Contains("Set-Cookie"));
            }

            using var next = await RefreshAsync(
                RefreshCookie(winner).Value.Value!,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ReusedTokenAfterTheGracePeriod_RevokesTheSession()
    {
        using var cancellation = CreateCancellation();
        var (_, first) = await RegisterAsync("reuse@example.com", cancellation.Token);
        using var refreshed = await RefreshAsync(first, cancellation.Token);
        var second = RefreshCookie(refreshed).Value.Value!;
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RefreshTokens
                .Where(token => token.ConsumedAt != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        token => token.ConsumedAt,
                        token => token.ConsumedAt!.Value.AddMinutes(-1)),
                    cancellation.Token);
        }

        using var reused = await RefreshAsync(first, cancellation.Token);
        using var afterwards = await RefreshAsync(second, cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        AssertCookieCleared(reused);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
        await using var verification = CreateDbContext();
        var session = await verification.AuthSessions.SingleAsync(cancellation.Token);
        Assert.Equal(AuthSessionRevocationReason.RefreshTokenReused, session.RevokedReason);
    }

    [Fact]
    public async Task AccessToken_IsRejectedOnceTheSessionIsIdleExpired()
    {
        using var cancellation = CreateCancellation();
        var (auth, _) = await RegisterAsync("idle@example.com", cancellation.Token);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.AuthSessions.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.IdleExpiresAt,
                    DateTimeOffset.UtcNow.AddMinutes(-1)),
                cancellation.Token);
        }

        using var client = Bearer(auth.AccessToken);
        using var response = await client.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/auth/logout")]
    public async Task Preflight_FromTheAllowedOrigin_AllowsCredentialsAndTheCsrfHeader(string path)
    {
        using var cancellation = CreateCancellation();

        using var response = await PreflightAsync(path, Origin, cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Origin, Single(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Single(response, "Access-Control-Allow-Credentials"));
        var allowedHeaders = Single(response, "Access-Control-Allow-Headers")
            .Split(',', StringSplitOptions.TrimEntries);
        Assert.Contains(CsrfHeader, allowedHeaders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Content-Type", allowedHeaders, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://app.example.com.evil.example")]
    [InlineData("http://app.example.com")]
    [InlineData("null")]
    public async Task Preflight_FromAnotherOrigin_GetsNoCorsHeaders(string origin)
    {
        using var cancellation = CreateCancellation();

        using var response = await PreflightAsync("/api/auth/refresh", origin, cancellation.Token);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private async Task<(AuthResponse Auth, string Cookie)> RegisterAsync(
        string email,
        CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync(
            "/api/auth/register",
            new { firstName = "Cookie", lastName = "User", email, password = Password },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAuthAsync(response, cancellationToken), RefreshCookie(response).Value.Value!);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var client = Application.CreateHttpsClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Origin", Origin);
        return await client.SendAsync(request, cancellationToken);
    }

    private Task<HttpResponseMessage> RefreshAsync(
        string? cookie,
        CancellationToken cancellationToken) =>
        SendCookieRequestAsync(
            "/api/auth/refresh",
            cookie,
            "1",
            Origin,
            referer: null,
            cancellationToken);

    private async Task<HttpResponseMessage> SendCookieRequestAsync(
        string path,
        string? cookie,
        string? csrfHeader,
        string? origin,
        string? referer,
        CancellationToken cancellationToken)
    {
        using var client = Application.CreateHttpsClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        if (csrfHeader is not null)
        {
            request.Headers.Add(CsrfHeader, csrfHeader);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (referer is not null)
        {
            request.Headers.Referrer = new Uri(referer);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> PreflightAsync(
        string path,
        string origin,
        CancellationToken cancellationToken)
    {
        using var client = Application.CreateHttpsClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-resumatch-csrf");
        return await client.SendAsync(request, cancellationToken);
    }

    private HttpClient Bearer(string token)
    {
        var client = Application.CreateHttpsClient(handleCookies: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static SetCookieHeaderValue RefreshCookie(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var values),
            "The response sets no cookie.");
        return Assert.Single(
            SetCookieHeaderValue.ParseList(values.ToList()),
            cookie => cookie.Name.Value == CookieName);
    }

    private static void AssertCookieCleared(HttpResponseMessage response)
    {
        var cookie = RefreshCookie(response);
        Assert.Equal(string.Empty, cookie.Value.Value);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        Assert.True(cookie.Secure);
        Assert.Equal("/", cookie.Path.Value);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    private static async Task<AuthResponse> ReadAuthAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options, cancellationToken))!;

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.GetValues(header));

    private static Guid SessionId(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims.Single(claim => claim.Type == "session").Value);
}
