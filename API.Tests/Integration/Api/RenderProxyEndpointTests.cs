using System.Net;
using System.Net.Http.Json;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api;

/// <summary>
/// Reproduces the Render topology: TLS ends at the edge, the API receives plain
/// HTTP from a private or loopback peer, and Cloudflare sets CF-Connecting-IP.
/// A fresh host per test isolates the in-memory limiters and the one-hour window
/// means no permit can replenish while a test runs.
/// </summary>
public sealed class RenderProxyEndpointTests : ApiIntegrationTestBase
{
    private const int LoginPermitLimit = 2;
    private const string SocketAddressHeader = "X-Test-Socket-Address";
    private const string InternalPeer = "10.1.2.3";
    private const string PublicPeer = "203.0.113.9";

    public RenderProxyEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected override ResuMatchWebApplicationFactory CreateApplication() =>
        ProductionApplication.Create(
            Fixture,
            new Dictionary<string, string?>
            {
                ["HttpSecurity:ForwardedForHeaderName"] = "CF-Connecting-IP",
                ["HttpSecurity:TrustedProxies:0"] = "127.0.0.1",
                ["HttpSecurity:TrustedProxies:1"] = "::1",
                ["HttpSecurity:TrustedNetworks:0"] = "10.0.0.0/8",
                ["AuthenticationRateLimits:LoginPermitLimit"] = "2",
                ["AuthenticationRateLimits:LoginWindow"] = "01:00:00"
            },
            services => services.AddSingleton<IStartupFilter, SocketAddressStartupFilter>());

    [Theory]
    [InlineData(InternalPeer)]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:10.1.2.3")]
    public async Task TrustedPeer_PartitionsLoginLimitByCloudflareClientAddress(string peer)
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(peer);
        await ExhaustLoginLimitAsync(client, "198.51.100.10", cancellation.Token);

        using var limited = await LoginAsync(client, "198.51.100.10", cancellation.Token);
        using var otherClient = await LoginAsync(client, "198.51.100.11", cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
    }

    [Fact]
    public async Task SpoofedForwardedForEntries_CannotEscapeTheLimit()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(InternalPeer);

        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(
                client,
                "198.51.100.20",
                cancellation.Token,
                forwardedFor: $"192.0.2.{attempt}, 198.51.100.20, 172.71.0.1, 10.0.0.1");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await LoginAsync(
            client,
            "198.51.100.20",
            cancellation.Token,
            forwardedFor: "192.0.2.99, 198.51.100.20, 172.71.0.1, 10.0.0.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task UntrustedPeer_CannotChooseItsClientAddress()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(PublicPeer);

        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(
                client,
                $"198.51.100.{30 + attempt}",
                cancellation.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await LoginAsync(client, "198.51.100.99", cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData("https")]
    [InlineData("https,https")]
    [InlineData("http, https")]
    public async Task ForwardedHttps_EnablesHstsWithoutRedirect(string forwardedProto)
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(InternalPeer);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("CF-Connecting-IP", "198.51.100.40");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "max-age=2592000",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task PlatformHealthCheckWithoutForwardedHeaders_IsServedWithoutRedirect()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(InternalPeer);

        using var response = await client.GetAsync("/health/live", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task UntrustedPeer_CannotClaimHttps()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient(PublicPeer);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private HttpClient CreateClient(string socketAddress)
    {
        var client = ProductionApplication.CreateClient(Application, scheme: "http");
        client.DefaultRequestHeaders.Add(SocketAddressHeader, socketAddress);
        return client;
    }

    private static async Task ExhaustLoginLimitAsync(
        HttpClient client,
        string clientAddress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(client, clientAddress, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string clientAddress,
        CancellationToken cancellationToken,
        string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(
                new { email = "nobody@example.com", password = "wrong-password" })
        };
        request.Headers.TryAddWithoutValidation("CF-Connecting-IP", clientAddress);
        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return client.SendAsync(request, cancellationToken);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(45));

    private sealed class SocketAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(
                            SocketAddressHeader,
                            out var value))
                    {
                        context.Connection.RemoteIpAddress =
                            IPAddress.Parse(value.ToString());
                    }

                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
