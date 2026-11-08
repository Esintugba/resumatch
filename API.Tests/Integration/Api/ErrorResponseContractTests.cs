using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Entities;
using Api.Infrastructure.ExceptionHandling;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

/// <summary>
/// The OpenAPI document must describe error responses as the API sends them,
/// because the web client's types are generated from it. The document tests
/// check its shape; the response tests send real failing requests and check
/// each answer against the document, so a change on either side fails here.
/// </summary>
public sealed partial class ErrorResponseContractTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ProblemJson = "application/problem+json";

    // Endpoints the fallback authorization policy does not cover.
    private static readonly HashSet<string> AnonymousOperations =
    [
        "GET /health/version",
        "GET /api/capabilities",
        "POST /api/auth/register",
        "POST /api/auth/login",
        "POST /api/auth/refresh",
        "POST /api/auth/logout"
    ];

    [Fact]
    public async Task Document_DescribesEveryErrorResponseAsProblemJson()
    {
        using var document = await GetDocumentAsync();
        var operations = Operations(document.RootElement).ToList();
        Assert.NotEmpty(operations);

        foreach (var (name, operation) in operations)
        {
            var responses = operation.GetProperty("responses");
            var statuses = responses.EnumerateObject()
                .Select(response => int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet();

            Assert.True(statuses.Contains(500), $"{name} does not document 500.");
            Assert.True(
                statuses.Contains(401) || AnonymousOperations.Contains(name),
                $"{name} requires a user but does not document 401.");

            foreach (var status in statuses.Where(status => status >= 400))
            {
                var content = responses.GetProperty(status.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .GetProperty("content");
                var mediaTypes = content.EnumerateObject().Select(media => media.Name).ToArray();
                Assert.True(
                    mediaTypes.SequenceEqual([ProblemJson]),
                    $"{name} {status} has media types {string.Join(", ", mediaTypes)}.");
                Assert.Equal(
                    "#/components/schemas/" + ErrorResponseSchemas.ForStatus(status),
                    content.GetProperty(ProblemJson).GetProperty("schema").GetProperty("$ref").GetString());
            }
        }
    }

    [Fact]
    public async Task Document_ErrorCodes_MatchApiErrorCodesAndTheDocs()
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var documentedInMarkdown = ReadCodesFromErrorCodesMarkdown();

        foreach (var name in new[]
        {
            ErrorResponseSchemas.ProblemDetails,
            ErrorResponseSchemas.ValidationProblemDetails,
            ErrorResponseSchemas.UnsupportedMediaTypeProblemDetails
        })
        {
            var schema = schemas.GetProperty(name);
            var codes = schema.GetProperty("properties").GetProperty("code").GetProperty("enum")
                .EnumerateArray().Select(code => code.GetString()!).ToHashSet();
            Assert.Equal(ErrorResponseSchemas.Codes.ToHashSet(), codes);
            Assert.Equal(documentedInMarkdown, codes);

            var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(
                name != ErrorResponseSchemas.UnsupportedMediaTypeProblemDetails,
                required.Contains("code"));
            Assert.Equal(
                name == ErrorResponseSchemas.ValidationProblemDetails,
                schema.GetProperty("properties").TryGetProperty("errors", out _));
        }
    }

    [Fact]
    public async Task Responses_MatchTheDocument()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = cancellation.Token;
        using var application = new ResuMatchWebApplicationFactory(Fixture, environmentName: "Development");
        using var anonymous = application.CreateHttpsClient();
        using var document = await GetDocumentAsync(anonymous, token);
        var clients = new AuthenticatedHttpClientFactory(application);
        using var user = await clients.CreateAsync(NewUser("errors@example.com"), token);

        async Task Check(string operation, HttpClient client, HttpRequestMessage request, int expectedStatus, string? expectedCode)
        {
            using (request)
            using (var response = await client.SendAsync(request, token))
            {
                await AssertMatchesDocumentAsync(document.RootElement, operation, response, expectedStatus, expectedCode, token);
            }
        }

        // 400: model validation, with field messages.
        await Check("POST /api/auth/login", anonymous,
            Json(HttpMethod.Post, "/api/auth/login", "{}"), 400, "validation_failed");
        // 400: an empty GUID passes the route constraint and the service rejects it.
        await Check("GET /api/resumes/{resumeId}", user,
            new HttpRequestMessage(HttpMethod.Get, $"/api/resumes/{Guid.Empty}"), 400, "validation_failed");
        // 401: wrong credentials and a missing token.
        await Check("POST /api/auth/login", anonymous,
            Json(HttpMethod.Post, "/api/auth/login", """{"email":"nobody@example.com","password":"Wrong-password-1"}"""),
            401, "unauthorized");
        await Check("GET /api/resumes", anonymous,
            new HttpRequestMessage(HttpMethod.Get, "/api/resumes"), 401, "unauthorized");
        // 403: refresh without the CSRF header.
        await Check("POST /api/auth/refresh", anonymous,
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh"), 403, "csrf_rejected");
        // 404: a resume that does not exist.
        await Check("GET /api/resumes/{resumeId}", user,
            new HttpRequestMessage(HttpMethod.Get, $"/api/resumes/{Guid.NewGuid()}"), 404, "not_found");
        // 413: a body over the authentication limit.
        await Check("POST /api/auth/login", anonymous,
            Json(HttpMethod.Post, "/api/auth/login", $$"""{"email":"{{new string('a', 20_000)}}"}"""),
            413, "payload_too_large");
        // 415: a form body, rejected by the API with a code...
        await Check("POST /api/auth/login", anonymous,
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = new FormUrlEncodedContent([new("email", "a@example.com")])
            },
            415, "unsupported_media_type");
        // ...and a non-JSON body, rejected by MVC without one.
        await Check("POST /api/auth/login", anonymous,
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = new StringContent("{}", Encoding.UTF8, "text/plain")
            },
            415, expectedCode: null);

        // 409, 428 and 412 need existing state.
        await Check("POST /api/auth/register", anonymous,
            Json(HttpMethod.Post, "/api/auth/register",
                """{"firstName":"Error","lastName":"Test","email":"errors@example.com","password":"Correct-horse-9"}"""),
            409, "registration_rejected");

        using var created = await user.SendAsync(Json(HttpMethod.Post, "/api/resumes", """{"title":"Errors"}"""), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(token));
        var resumeId = createdBody.RootElement.GetProperty("id").GetGuid();
        const string update = """{"title":"Errors, updated"}""";

        await Check("PUT /api/resumes/{resumeId}", user,
            Json(HttpMethod.Put, $"/api/resumes/{resumeId}", update), 428, "precondition_required");
        var stale = Json(HttpMethod.Put, $"/api/resumes/{resumeId}", update);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"FFFFFFFF\"");
        await Check("PUT /api/resumes/{resumeId}", user, stale, 412, "precondition_failed");
    }

    [Fact]
    public async Task RateLimitedResponse_MatchesTheDocument()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AuthenticationRateLimits:LoginPermitLimit"] = "1"
            },
            environmentName: "Development");
        using var client = application.CreateHttpsClient();
        using var document = await GetDocumentAsync(client, cancellation.Token);
        const string body = """{"email":"nobody@example.com","password":"Wrong-password-1"}""";

        using var first = await client.SendAsync(Json(HttpMethod.Post, "/api/auth/login", body), cancellation.Token);
        using var limited = await client.SendAsync(Json(HttpMethod.Post, "/api/auth/login", body), cancellation.Token);

        await AssertMatchesDocumentAsync(
            document.RootElement, "POST /api/auth/login", limited, 429, "rate_limited", cancellation.Token);
    }

    private static async Task AssertMatchesDocumentAsync(
        JsonElement document,
        string operation,
        HttpResponseMessage response,
        int expectedStatus,
        string? expectedCode,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        Assert.True(status == expectedStatus, $"{operation}: expected {expectedStatus}, got {status}.");

        var (method, path) = SplitOperation(operation);
        var responses = document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");
        Assert.True(
            responses.TryGetProperty(status.ToString(System.Globalization.CultureInfo.InvariantCulture), out var documented),
            $"{operation} answered {status}, which the document does not list.");
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var schemaName = documented.GetProperty("content").GetProperty(ProblemJson)
            .GetProperty("schema").GetProperty("$ref").GetString()!.Split('/')[^1];
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        AssertConformsTo(schema, body.RootElement, $"{operation} {status}");

        if (expectedCode is null)
        {
            Assert.False(body.RootElement.TryGetProperty("code", out _), $"{operation} {status} has a code.");
        }
        else
        {
            Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
        }
    }

    // Checks the parts of the schema the error schemas use: required
    // properties, property types, the code enum and the errors map.
    private static void AssertConformsTo(JsonElement schema, JsonElement body, string context)
    {
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        foreach (var required in schema.GetProperty("required").EnumerateArray())
        {
            Assert.True(body.TryGetProperty(required.GetString()!, out _), $"{context}: missing {required}.");
        }

        var properties = schema.GetProperty("properties");
        foreach (var property in body.EnumerateObject())
        {
            Assert.True(properties.TryGetProperty(property.Name, out var propertySchema),
                $"{context}: property '{property.Name}' is not in the schema.");
            var types = propertySchema.GetProperty("type") is { ValueKind: JsonValueKind.Array } list
                ? list.EnumerateArray().Select(type => type.GetString()!).ToArray()
                : [propertySchema.GetProperty("type").GetString()!];
            Assert.True(types.Contains(JsonTypeName(property.Value)),
                $"{context}: '{property.Name}' is {property.Value.ValueKind}, schema says {string.Join("|", types)}.");

            if (propertySchema.TryGetProperty("enum", out var values))
            {
                Assert.Contains(property.Value.GetString(), values.EnumerateArray().Select(value => value.GetString()));
            }

            if (property.Name == "errors")
            {
                Assert.All(property.Value.EnumerateObject(), field =>
                    Assert.All(field.Value.EnumerateArray(), message =>
                        Assert.Equal(JsonValueKind.String, message.ValueKind)));
            }
        }
    }

    private static string JsonTypeName(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "integer",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.Null => "null",
        _ => value.ValueKind.ToString()
    };

    private static IEnumerable<(string Name, JsonElement Operation)> Operations(JsonElement document)
    {
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                yield return ($"{operation.Name.ToUpperInvariant()} {path.Name}", operation.Value);
            }
        }
    }

    private static (string Method, string Path) SplitOperation(string operation)
    {
        var space = operation.IndexOf(' ', StringComparison.Ordinal);
        return (operation[..space].ToLowerInvariant(), operation[(space + 1)..]);
    }

    private static HashSet<string> ReadCodesFromErrorCodesMarkdown()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ResuMatch.slnx")))
        {
            directory = directory.Parent;
        }

        var markdown = File.ReadAllText(Path.Combine(directory.FullName, "docs", "api", "error-codes.md"));
        return CodeTableRow().Matches(markdown).Select(match => match.Groups[1].Value).ToHashSet();
    }

    // The first cell of a code table row: | `code` | ...
    [GeneratedRegex(@"^\| `([a-z_]+)` \|", RegexOptions.Multiline)]
    private static partial Regex CodeTableRow();

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var application = new ResuMatchWebApplicationFactory(Fixture, environmentName: "Development");
        using var client = application.CreateHttpsClient();
        return await GetDocumentAsync(client, cancellation.Token);
    }

    private static async Task<JsonDocument> GetDocumentAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string json) =>
        new(method, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static User NewUser(string email) => new()
    {
        FirstName = "Error",
        LastName = "Test",
        Email = email,
        PasswordHash = "integration-test-hash",
        IsActive = true
    };
}
