using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.DTOs.Auth;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Api;

public sealed class AuthSessionEndpointTests : AnalysisApiTestBase
{
    private const string Password = "Correct-Horse-Battery-1";
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    public AuthSessionEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Login_OpensASessionAndPutsItInTheToken()
    {
        using var cancellation = CreateCancellation();
        await RegisterAsync("session@example.com", cancellation.Token);

        var login = await LoginAsync("session@example.com", cancellation.Token);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var sessionId = Guid.Parse(token.Claims.Single(claim => claim.Type == "session").Value);
        Assert.True(Guid.TryParse(token.Claims.Single(claim => claim.Type == "stamp").Value, out _));
        Assert.DoesNotContain(token.Claims, claim => claim.Value.Length == 43);
        await using var dbContext = CreateDbContext();
        var session = await dbContext.AuthSessions.AsNoTracking().SingleAsync(item => item.Id == sessionId);
        Assert.Equal("Chrome on Windows", session.DeviceLabel);
        Assert.Equal(2, await dbContext.AuthSessions.CountAsync());
        Assert.Equal(0, await dbContext.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Sessions_ListsActiveSessionsAndMarksTheCurrentOne()
    {
        using var cancellation = CreateCancellation();
        await RegisterAsync("list@example.com", cancellation.Token);
        var other = await LoginAsync("list@example.com", cancellation.Token);
        var current = await LoginAsync("list@example.com", cancellation.Token);
        using var client = Bearer(current.AccessToken);

        var sessions = await client.GetFromJsonAsync<List<AuthSessionResponse>>(
            "/api/auth/sessions",
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(3, sessions!.Count);
        var marked = Assert.Single(sessions, item => item.IsCurrent);
        Assert.Equal(SessionId(current.AccessToken), marked.Id);
        Assert.Contains(sessions, item => item.Id == SessionId(other.AccessToken));
        Assert.All(sessions, item => Assert.Equal("Chrome on Windows", item.DeviceLabel));
    }

    [Fact]
    public async Task OrdinaryRequests_DoNotWriteSessionState()
    {
        using var cancellation = CreateCancellation();
        var auth = await RegisterAsync("quiet@example.com", cancellation.Token);
        using var client = Bearer(auth.AccessToken);
        var before = await SessionRowAsync(SessionId(auth.AccessToken), cancellation.Token);

        for (var request = 0; request < 3; request++)
        {
            using var response = await client.GetAsync("/api/auth/me", cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var after = await SessionRowAsync(SessionId(auth.AccessToken), cancellation.Token);
        Assert.Equal(before.LastUsedAt, after.LastUsedAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.Xmin, after.Xmin);
    }

    [Fact]
    public async Task RevokingTheCurrentSession_StopsItsAccessTokenImmediately()
    {
        using var cancellation = CreateCancellation();
        var auth = await RegisterAsync("revoke@example.com", cancellation.Token);
        var other = await LoginAsync("revoke@example.com", cancellation.Token);
        using var client = Bearer(auth.AccessToken);
        using var otherClient = Bearer(other.AccessToken);

        using var revoked = await client.DeleteAsync(
            $"/api/auth/sessions/{SessionId(auth.AccessToken):D}",
            cancellation.Token);
        using var afterwards = await client.GetAsync("/api/auth/me", cancellation.Token);
        using var stillValid = await otherClient.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task RevokingAnotherUsersSession_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var victim = await RegisterAsync("victim@example.com", cancellation.Token);
        var attacker = await RegisterAsync("attacker@example.com", cancellation.Token);
        using var client = Bearer(attacker.AccessToken);

        using var response = await client.DeleteAsync(
            $"/api/auth/sessions/{SessionId(victim.AccessToken):D}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var victimClient = Bearer(victim.AccessToken);
        using var stillValid = await victimClient.GetAsync("/api/auth/me", cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task LogoutAll_InvalidatesEveryAccessToken()
    {
        using var cancellation = CreateCancellation();
        var first = await RegisterAsync("everywhere@example.com", cancellation.Token);
        var second = await LoginAsync("everywhere@example.com", cancellation.Token);
        using var client = Bearer(first.AccessToken);

        using var response = await client.PostAsync("/api/auth/logout-all", content: null, cancellation.Token);
        using var firstAfter = await client.GetAsync("/api/auth/me", cancellation.Token);
        using var secondClient = Bearer(second.AccessToken);
        using var secondAfter = await secondClient.GetAsync("/api/auth/me", cancellation.Token);
        var third = await LoginAsync("everywhere@example.com", cancellation.Token);
        using var thirdClient = Bearer(third.AccessToken);
        using var thirdAfter = await thirdClient.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, firstAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thirdAfter.StatusCode);
        await using var dbContext = CreateDbContext();
        Assert.Equal(
            2,
            await dbContext.AuthSessions.CountAsync(item =>
                item.RevokedReason == AuthSessionRevocationReason.SignedOutEverywhere));
    }

    [Fact]
    public async Task TokenWithoutSessionOrStamp_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(1, "legacy@example.com");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }

        var now = DateTimeOffset.UtcNow;
        using var client = Bearer(AuthenticatedHttpClientFactory.CreateControlledToken(
            user.Id,
            now.AddMinutes(-1),
            now.AddMinutes(5)));

        using var response = await client.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangedSecurityStamp_RejectsEarlierTokens()
    {
        using var cancellation = CreateCancellation();
        var auth = await RegisterAsync("stamp@example.com", cancellation.Token);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Users.ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.SecurityStamp, Guid.NewGuid()),
                cancellation.Token);
        }

        using var client = Bearer(auth.AccessToken);
        using var response = await client.GetAsync("/api/auth/me", cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginResponse_DoesNotContainARefreshToken()
    {
        using var cancellation = CreateCancellation();
        await RegisterAsync("body@example.com", cancellation.Token);
        using var client = Application.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "body@example.com", password = Password },
            cancellation.Token);

        var body = await response.Content.ReadAsStringAsync(cancellation.Token);
        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private HttpClient Bearer(string token)
    {
        var client = Application.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Guid SessionId(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims.Single(claim => claim.Type == "session").Value);

    private async Task<(DateTimeOffset LastUsedAt, DateTimeOffset? UpdatedAt, long Xmin)> SessionRowAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        var row = await dbContext.Database
            .SqlQuery<SessionRow>(
                $"""SELECT "LastUsedAt", "UpdatedAt", xmin::text::bigint AS "Xmin" FROM "AuthSessions" WHERE "Id" = {sessionId}""")
            .SingleAsync(cancellationToken);
        return (row.LastUsedAt, row.UpdatedAt, row.Xmin);
    }

    private async Task<AuthResponse> RegisterAsync(string email, CancellationToken cancellationToken)
    {
        using var client = Application.CreateHttpsClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeOnWindows);
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { firstName = "Session", lastName = "User", email, password = Password },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options, cancellationToken))!;
    }

    private async Task<AuthResponse> LoginAsync(string email, CancellationToken cancellationToken)
    {
        using var client = Application.CreateHttpsClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeOnWindows);
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = Password },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options, cancellationToken))!;
    }

    private sealed record SessionRow(DateTimeOffset LastUsedAt, DateTimeOffset? UpdatedAt, long Xmin);
}
