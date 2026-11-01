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

    [Fact]
    public async Task MalformedChunkedBody_Returns400ProblemDetailsWithoutDetails()
    {
        // Kestrel throws BadHttpRequestException("Bad chunk size data") when the
        // endpoint reads this body; HttpClient cannot send it, so use raw TCP.
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        var request =
            "POST /api/resumes HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            $"Authorization: Bearer {server.AccessToken}\r\n" +
            "Content-Type: application/json\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "ZZ\r\n{}\r\n0\r\n\r\n";

        var response = await RawHttp.SendAsync(server.Address, request, cancellation.Token);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(
            "Content-Type: application/problem+json",
            response.Headers,
            StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        var body = document.RootElement;
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Equal(
            "The request could not be processed because it was malformed or incomplete.",
            body.GetProperty("detail").GetString());
        Assert.DoesNotContain("chunk", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedRequestHeaders_AreRejectedWith431NotServerError()
    {
        // Kestrel answers this itself before the application pipeline runs;
        // the test pins that the client sees a 4xx and never a 5xx.
        using var cancellation = CreateCancellation();
        using var server = await KestrelServer.StartAsync(Fixture, cancellation.Token);
        var request =
            "GET /health/live HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            $"X-Oversized: {new string('a', 40_000)}\r\n" +
            "\r\n";

        var response = await RawHttp.SendAsync(server.Address, request, cancellation.Token);

        Assert.Equal(431, response.StatusCode);
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
                ["code"] = "payload_too_large",
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
            Uri address,
            string accessToken,
            HttpClient anonymousClient,
            HttpClient authenticatedClient)
        {
            _application = application;
            Address = address;
            AccessToken = accessToken;
            AnonymousClient = anonymousClient;
            AuthenticatedClient = authenticatedClient;
        }

        public Uri Address { get; }
        public string AccessToken { get; }
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

                return new KestrelServer(
                    application,
                    new Uri(address),
                    token,
                    anonymous,
                    authenticated);
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

    private static class RawHttp
    {
        public static async Task<RawHttpResponse> SendAsync(
            Uri address,
            string request,
            CancellationToken cancellationToken)
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(address.Host, address.Port, cancellationToken);
            await using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);

            // Kestrel closes the connection after a bad request, so read to EOF.
            using var received = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                received.Write(buffer, 0, read);
            }

            var text = Encoding.UTF8.GetString(received.ToArray());
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            Assert.True(headerEnd > 0, "No HTTP response headers were received.");
            var headers = text[..headerEnd];
            var body = text[(headerEnd + 4)..];
            var statusCode = int.Parse(
                headers.Split(' ', 3)[1],
                System.Globalization.CultureInfo.InvariantCulture);
            if (headers.Contains(
                    "Transfer-Encoding: chunked",
                    StringComparison.OrdinalIgnoreCase))
            {
                body = Dechunk(body);
            }

            return new RawHttpResponse(statusCode, headers, body);
        }

        private static string Dechunk(string chunked)
        {
            var result = new StringBuilder();
            var position = 0;
            while (true)
            {
                var lineEnd = chunked.IndexOf("\r\n", position, StringComparison.Ordinal);
                var size = Convert.ToInt32(chunked[position..lineEnd], 16);
                if (size == 0)
                {
                    return result.ToString();
                }

                result.Append(chunked, lineEnd + 2, size);
                position = lineEnd + 2 + size + 2;
            }
        }
    }

    private sealed record RawHttpResponse(int StatusCode, string Headers, string Body);

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
