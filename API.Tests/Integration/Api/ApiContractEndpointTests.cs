using System.Net;
using System.Text;
using System.Text.Json;
using Api.Application.Common.Exceptions;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class ApiContractEndpointTests : AnalysisApiTestBase
{
    private const string WorkTypeError = "Must be one of: OnSite, Hybrid, Remote.";

    public ApiContractEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Responses_WriteEnumsAsNames()
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await PostJobPostingAsync(
            client,
            "\"workType\":\"Remote\",",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellation.Token);
        Assert.Equal("Remote", body.RootElement.GetProperty("workType").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("status").ValueKind);
    }

    [Theory]
    [InlineData("\"Teleport\"")]
    [InlineData("99")]
    [InlineData("1")]
    public async Task Body_UnknownOrNumericEnum_ReturnsClearValidationError(string value)
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await PostJobPostingAsync(
            client,
            $"\"workType\":{value},",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellation.Token);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            body.RootElement.GetProperty("code").GetString());
        Assert.Contains(
            body.RootElement.GetProperty("errors").EnumerateObject(),
            field => field.Value.EnumerateArray().Any(
                message => message.GetString() == WorkTypeError));
        Assert.DoesNotContain("Teleport", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Api.Entities", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Teleport")]
    [InlineData("1")]
    public async Task Query_UnknownOrNumericEnum_ReturnsClearValidationError(string value)
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.GetAsync(
            $"/api/job-applications?status={value}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellation.Token);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            body.RootElement.GetProperty("code").GetString());
        var message = Assert.Single(
            body.RootElement.GetProperty("errors").GetProperty("status").EnumerateArray());
        Assert.Equal(
            "Must be one of: " +
            string.Join(", ", Enum.GetNames<ApplicationStatus>()) + ".",
            message.GetString());
    }

    [Fact]
    public async Task Query_EnumName_IsAccepted()
    {
        using var cancellation = CreateCancellation();
        var user = await SeedUserAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.GetAsync(
            $"/api/job-applications?status={nameof(ApplicationStatus.Applied)}",
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingToken_ReturnsProblemWithCode()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();

        using var response = await client.GetAsync("/api/resumes", cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        using var body = await ReadJsonAsync(response, cancellation.Token);
        Assert.Equal(
            ApiErrorCodes.Unauthorized,
            body.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public void EveryErrorCode_IsDocumented()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ResuMatch.slnx")))
        {
            directory = directory.Parent;
        }

        var document = File.ReadAllText(
            Path.Combine(directory.FullName, "docs", "api", "error-codes.md"));
        var codes = typeof(ApiErrorCodes)
            .GetFields()
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.All(codes, code =>
            Assert.Contains($"`{code}`", document, StringComparison.Ordinal));
    }

    private async Task<User> SeedUserAsync(CancellationToken cancellationToken)
    {
        var user = CreateUser(1, "contract@example.com");
        await using var dbContext = CreateDbContext();
        dbContext.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private static Task<HttpResponseMessage> PostJobPostingAsync(
        HttpClient client,
        string extraFields,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            "/api/job-postings",
            new StringContent(
                $$"""{{{extraFields}}"title":"Engineer","description":"C# is required."}""",
                Encoding.UTF8,
                "application/json"),
            cancellationToken);

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
}
