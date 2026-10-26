using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Application.Common.Interfaces;
using Api.Entities;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api;

/// <summary>
/// Runs the API on real Kestrel (dynamic loopback port) because TestServer does
/// not enforce the server-wide request body limit.
/// </summary>
public sealed class KestrelBodyLimitEndpointTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const int GlobalBodyLimit = 1_048_576;
    private const int AuthenticationBodyLimit = 16_384;

    [Fact]
    public async Task ContentLengthAboveGlobalLimit_Returns413ProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        using var content = new ByteArrayContent(JsonBytesOfSize(GlobalBodyLimit + 1));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await server.AuthenticatedClient.PostAsync(
            "/api/resumes",
            content,
            cancellation.Token);

        await AssertPayloadTooLargeAsync(response, "/api/resumes", cancellation.Token);
    }

    [Fact]
    public async Task ChunkedBodyAboveGlobalLimit_Returns413ProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        using var content = new UnknownLengthContent(JsonBytesOfSize(GlobalBodyLimit + 1));

        using var response = await server.AuthenticatedClient.PostAsync(
            "/api/resumes",
            content,
            cancellation.Token);

        Assert.Null(content.Headers.ContentLength);
        await AssertPayloadTooLargeAsync(response, "/api/resumes", cancellation.Token);
    }

    [Fact]
    public async Task ServerLevelAndMiddleware413Responses_AreIdentical()
    {
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        using var declared = new ByteArrayContent(JsonBytesOfSize(GlobalBodyLimit + 1));
        declared.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var chunked = new UnknownLengthContent(JsonBytesOfSize(GlobalBodyLimit + 1));

        using var middlewareResponse = await server.AuthenticatedClient.PostAsync(
            "/api/resumes",
            declared,
            cancellation.Token);
        using var serverResponse = await server.AuthenticatedClient.PostAsync(
            "/api/resumes",
            chunked,
            cancellation.Token);

        Assert.Equal(middlewareResponse.StatusCode, serverResponse.StatusCode);
        Assert.Equal(
            middlewareResponse.Content.Headers.ContentType?.ToString(),
            serverResponse.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            await middlewareResponse.Content.ReadAsStringAsync(cancellation.Token),
            await serverResponse.Content.ReadAsStringAsync(cancellation.Token));
    }

    [Fact]
    public async Task ChunkedAuthenticationBodyAboveLimit_Returns413ProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        using var content = new UnknownLengthContent(
            JsonBytesOfSize(AuthenticationBodyLimit * 4));

        using var response = await server.AnonymousClient.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        await AssertPayloadTooLargeAsync(response, "/api/auth/login", cancellation.Token);
    }

    private static async Task AssertPayloadTooLargeAsync(
        HttpResponseMessage response,
        string expectedInstance,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var properties = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.ToString());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["title"] = "Payload Too Large",
                ["status"] = "413",
                ["detail"] = "The request body exceeds the permitted size.",
                ["instance"] = expectedInstance
            },
            properties);
        SecurityHeadersEndpointTests.AssertSecurityHeaders(response);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private static byte[] JsonBytesOfSize(int size)
    {
        var prefix = Encoding.UTF8.GetBytes(
            "{\"title\":\"Body limit\",\"professionalTitle\":\"Engineer\"}");
        var bytes = new byte[size];
        prefix.CopyTo(bytes, 0);
        bytes.AsSpan(prefix.Length).Fill((byte)' ');
        return bytes;
    }

    private sealed class KestrelServer : IDisposable
    {
        private readonly ResuMatchWebApplicationFactory _application;

        private KestrelServer(
            ResuMatchWebApplicationFactory application,
            HttpClient anonymousClient,
            HttpClient authenticatedClient)
        {
            _application = application;
            AnonymousClient = anonymousClient;
            AuthenticatedClient = authenticatedClient;
        }

        public HttpClient AnonymousClient { get; }
        public HttpClient AuthenticatedClient { get; }

        public static async Task<KestrelServer> StartAsync(
            PostgreSqlFixture fixture,
            CancellationToken cancellationToken)
        {
            var application = new ResuMatchWebApplicationFactory(fixture);
            try
            {
                application.UseKestrel(0);
                application.StartServer();

                var address = application.Services
                    .GetRequiredService<IServer>()
                    .Features
                    .Get<IServerAddressesFeature>()!
                    .Addresses
                    .First();
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Kestrel",
                    LastName = "Limit",
                    Email = "kestrel-limit@example.com",
                    PasswordHash = "integration-test-password-hash",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await using (var context = fixture.DbContextFactory.CreateDbContext())
                {
                    context.Users.Add(user);
                    await context.SaveChangesAsync(cancellationToken);
                }

                var token = application.Services
                    .GetRequiredService<IJwtTokenService>()
                    .GenerateAccessToken(user)
                    .Token;
                var anonymous = CreateClient(address);
                var authenticated = CreateClient(address);
                authenticated.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                return new KestrelServer(application, anonymous, authenticated);
            }
            catch
            {
                application.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            AnonymousClient.Dispose();
            AuthenticatedClient.Dispose();
            _application.Dispose();
        }

        private static HttpClient CreateClient(string address) =>
            new(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address)
            };
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(byte[] bytes)
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
