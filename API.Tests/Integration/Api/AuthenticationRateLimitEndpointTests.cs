using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Api.Infrastructure.Authentication;
using Api.Infrastructure.Security;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api;

/// <summary>
/// Isolation: every test gets a new <see cref="ResuMatchWebApplicationFactory"/>
/// (and therefore new in-memory rate limiters) from
/// <see cref="ApiIntegrationTestBase"/>. Inside a test, each scenario uses its
/// own client address, which is the login/register partition key. Windows are
/// one hour long, so no permit can replenish while a test runs and no test
/// needs to wait for time to pass.
/// </summary>
public sealed class AuthenticationRateLimitEndpointTests : ApiIntegrationTestBase
{
    private const int LoginPermitLimit = 2;
    private const int RegisterPermitLimit = 2;
    private const string ClientAddressHeader = "X-Test-Client-Address";
    private const string RateLimitedInstrument = "resumatch.security.rate_limited";

    public AuthenticationRateLimitEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected override ResuMatchWebApplicationFactory CreateApplication() =>
        new(
            Fixture,
            new Dictionary<string, string?>
            {
                ["AuthenticationRateLimits:LoginPermitLimit"] =
                    LoginPermitLimit.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                ["AuthenticationRateLimits:LoginWindow"] = "01:00:00",
                ["AuthenticationRateLimits:RegisterPermitLimit"] =
                    RegisterPermitLimit.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                ["AuthenticationRateLimits:RegisterWindow"] = "01:00:00"
            },
            configureTestServices: services =>
                services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>());

    [Fact]
    public async Task Login_AboveLimit_Returns429WithRetryAfterAndProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient("198.51.100.10");

        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(client, cancellation.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await LoginAsync(client, cancellation.Token);

        await AssertRateLimitedAsync(limited, TimeSpan.FromHours(1), cancellation.Token);
    }

    [Fact]
    public async Task Register_AboveLimit_Returns429WithRetryAfterAndProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient("198.51.100.20");

        for (var attempt = 0; attempt < RegisterPermitLimit; attempt++)
        {
            using var allowed = await RegisterAsync(
                client,
                $"register-{attempt}@example.com",
                cancellation.Token);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        using var limited = await RegisterAsync(
            client,
            "register-limited@example.com",
            cancellation.Token);

        await AssertRateLimitedAsync(limited, TimeSpan.FromHours(1), cancellation.Token);
    }

    [Fact]
    public async Task RejectedRegister_DoesNotCreateUser()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient("198.51.100.30");
        for (var attempt = 0; attempt < RegisterPermitLimit; attempt++)
        {
            using var allowed = await RegisterAsync(
                client,
                $"quota-{attempt}@example.com",
                cancellation.Token);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        using var limited = await RegisterAsync(
            client,
            "quota-rejected@example.com",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(context.Users.Any(user => user.Email == "quota-rejected@example.com"));
    }

    [Fact]
    public async Task LoginAndRegister_HaveIndependentQuotas()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient("198.51.100.40");
        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var _ = await LoginAsync(client, cancellation.Token);
        }

        using var loginLimited = await LoginAsync(client, cancellation.Token);
        using var register = await RegisterAsync(
            client,
            "independent@example.com",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, loginLimited.StatusCode);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
    }

    [Fact]
    public async Task Login_IsPartitionedByClientAddress()
    {
        using var cancellation = CreateCancellation();
        using var first = CreateClient("198.51.100.50");
        using var second = CreateClient("198.51.100.51");
        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var _ = await LoginAsync(first, cancellation.Token);
        }

        using var firstLimited = await LoginAsync(first, cancellation.Token);
        using var secondAllowed = await LoginAsync(second, cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, firstLimited.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAllowed.StatusCode);
    }

    [Fact]
    public async Task Login_PathVariantsShareTheSameQuota()
    {
        using var cancellation = CreateCancellation();
        using var client = CreateClient("198.51.100.60");

        using var lower = await LoginAsync(client, cancellation.Token, "/api/auth/login");
        using var upper = await LoginAsync(client, cancellation.Token, "/API/Auth/Login");
        using var limited = await LoginAsync(
            client,
            cancellation.Token,
            "/api/auth/login?source=test");

        Assert.Equal(HttpStatusCode.Unauthorized, lower.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upper.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RateLimitedRequests_IncrementMetricWithBoundedPolicyTag()
    {
        using var cancellation = CreateCancellation();
        var policies = new ConcurrentQueue<(long Value, string? Policy)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ApiSecurityMetrics.MeterName &&
                instrument.Name == RateLimitedInstrument)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? policy = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "policy")
                {
                    policy = tag.Value as string;
                }
            }

            policies.Enqueue((value, policy));
        });
        listener.Start();

        using var loginClient = CreateClient("198.51.100.70");
        using var registerClient = CreateClient("198.51.100.71");
        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            using var _ = await LoginAsync(loginClient, cancellation.Token);
        }

        for (var attempt = 0; attempt < RegisterPermitLimit; attempt++)
        {
            using var _ = await RegisterAsync(
                registerClient,
                $"metric-{attempt}@example.com",
                cancellation.Token);
        }

        Assert.Empty(policies);

        using var loginLimited = await LoginAsync(
            loginClient,
            cancellation.Token,
            "/API/AUTH/LOGIN?attempt=3");
        using var registerLimited = await RegisterAsync(
            registerClient,
            "metric-limited@example.com",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, loginLimited.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, registerLimited.StatusCode);
        Assert.Equal(
            [
                (1L, (string?)AuthenticationRateLimitPolicyNames.Login),
                (1L, (string?)AuthenticationRateLimitPolicyNames.Register)
            ],
            policies.ToArray());
    }

    private HttpClient CreateClient(string clientAddress)
    {
        var client = Application.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(ClientAddressHeader, clientAddress);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        string path = "/api/auth/login") =>
        client.PostAsJsonAsync(
            path,
            new { email = "nobody@example.com", password = "wrong-password" },
            cancellationToken);

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                firstName = "Rate",
                lastName = "Limit",
                email,
                password = "Correct-Horse-Battery-1"
            },
            cancellationToken);

    private static async Task AssertRateLimitedAsync(
        HttpResponseMessage response,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), window);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            cancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Too Many Requests", problem.Title);
        SecurityHeadersEndpointTests.AssertSecurityHeaders(response);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(45));

    private sealed class TestClientAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(
                            ClientAddressHeader,
                            out var value) &&
                        IPAddress.TryParse(value.ToString(), out var address))
                    {
                        context.Connection.RemoteIpAddress = address;
                    }

                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
