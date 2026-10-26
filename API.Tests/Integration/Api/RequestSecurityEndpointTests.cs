using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Api.Entities;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests.Integration.Api;

public sealed class RequestSecurityEndpointTests : ApiIntegrationTestBase
{
    private const int AuthenticationBodyLimit = 16_384;
    private const int GlobalBodyLimit = 1_048_576;
    private const string FormDetail =
        "Form request bodies are not supported by this API.";

    public RequestSecurityEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/API/AUTH/LOGIN")]
    public async Task AuthenticationBodyAboveLimit_Returns413ProblemDetails(string path)
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = JsonBodyOfSize(AuthenticationBodyLimit + 1);

        using var response = await client.PostAsync(path, content, cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.",
            cancellation.Token);
    }

    [Fact]
    public async Task AuthenticationBodyAtLimit_IsNotRejectedBySizeGuard()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = JsonBodyOfSize(AuthenticationBodyLimit);

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAuthenticationBodyAboveAuthenticationLimit_UsesGlobalLimit()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = JsonBodyOfSize(AuthenticationBodyLimit * 2);

        using var response = await client.PostAsync(
            "/api/resumes",
            content,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BodyAboveGlobalLimit_Returns413BeforeAuthentication()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = JsonBodyOfSize(GlobalBodyLimit + 1);

        using var response = await client.PostAsync(
            "/api/resumes",
            content,
            cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.",
            cancellation.Token);
    }

    [Theory]
    [InlineData(AuthenticationBodyLimit + 1)]
    [InlineData(AuthenticationBodyLimit * 4)]
    public async Task ChunkedAuthenticationBodyAboveLimit_IsRejected(int size)
    {
        // A chunked request has no Content-Length, so the limit must be enforced
        // on the bytes read and produce the same response as the header check.
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new UnknownLengthContent(
            JsonBytesOfSize(size),
            "application/json");

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        Assert.Null(content.Headers.ContentLength);
        await AssertProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.",
            cancellation.Token);
    }

    [Theory]
    [InlineData(AuthenticationBodyLimit - 1)]
    [InlineData(AuthenticationBodyLimit)]
    public async Task ChunkedAuthenticationBodyAtOrBelowLimit_IsProcessedNormally(int size)
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new UnknownLengthContent(
            JsonBytesOfSize(size),
            "application/json");

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChunkedBodyAboveGlobalLimit_IsRejected()
    {
        // Authenticated, so the request reaches model binding and the body is read.
        using var cancellation = CreateCancellation();
        using var client = await new AuthenticatedHttpClientFactory(Application)
            .CreateAsync(
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Body",
                    LastName = "Limit",
                    Email = "chunked-global@example.com",
                    PasswordHash = "integration-test-password-hash",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                cancellation.Token);
        using var content = new UnknownLengthContent(
            JsonBytesOfSize(GlobalBodyLimit + 1),
            "application/json");

        using var response = await client.PostAsync(
            "/api/resumes",
            content,
            cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.",
            cancellation.Token);
    }

    [Fact]
    public async Task DeclaredLengthBelowLimitButMoreBytesSent_IsRejected()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new MisdeclaredLengthContent(
            JsonBytesOfSize(AuthenticationBodyLimit * 4),
            declaredLength: 1_024,
            "application/json");

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.",
            cancellation.Token);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/resumes")]
    public async Task UrlEncodedForm_Returns415ProblemDetails(string path)
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "form@example.com",
            ["password"] = "irrelevant-password"
        });

        using var response = await client.PostAsync(path, content, cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            "Unsupported Media Type",
            FormDetail,
            cancellation.Token);
    }

    [Fact]
    public async Task MultipartForm_Returns415ProblemDetails()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new MultipartFormDataContent
        {
            { new StringContent("form@example.com"), "email" },
            { new ByteArrayContent([1, 2, 3]), "file", "resume.pdf" }
        };

        using var response = await client.PostAsync(
            "/api/auth/register",
            content,
            cancellation.Token);

        await AssertProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            "Unsupported Media Type",
            FormDetail,
            cancellation.Token);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task NonJsonBody_Returns415(string mediaType)
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var content = new StringContent(
            "email=plain@example.com",
            Encoding.UTF8,
            mediaType);

        using var response = await client.PostAsync(
            "/api/auth/login",
            content,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        SecurityHeadersEndpointTests.AssertSecurityHeaders(response);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle,
        string expectedDetail,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            cancellationToken);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedTitle, problem.Title);
        Assert.Equal(expectedDetail, problem.Detail);
        SecurityHeadersEndpointTests.AssertSecurityHeaders(response);
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(45));

    private static ByteArrayContent JsonBodyOfSize(int size)
    {
        var content = new ByteArrayContent(JsonBytesOfSize(size));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static byte[] JsonBytesOfSize(int size)
    {
        // Valid JSON padded with insignificant whitespace, so only the size differs.
        var json = "{\"email\":\"size@example.com\",\"password\":\"wrong-password\"}";
        var prefix = Encoding.UTF8.GetBytes(json);
        if (prefix.Length > size)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var bytes = new byte[size];
        prefix.CopyTo(bytes, 0);
        bytes.AsSpan(prefix.Length).Fill((byte)' ');
        return bytes;
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(byte[] bytes, string mediaType)
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class MisdeclaredLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public MisdeclaredLengthContent(
            byte[] bytes,
            long declaredLength,
            string mediaType)
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            Headers.ContentLength = declaredLength;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? 0;
            return true;
        }
    }
}
