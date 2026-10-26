using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.DTOs.Analysis;
using Api.Entities;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

public sealed class AnalysisProblemDetailsTests : AnalysisApiTestBase
{
    public AnalysisProblemDetailsTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PostAnalysis_WithMalformedGuid_ReturnsValidationProblemDetails()
    {
        using var cancellation = CreateCancellation();
        var (user, _) = await SeedUserAndResumeAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        using var content = new StringContent(
            """{"resumeId":"not-a-guid"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/analyses",
            content,
            cancellation.Token);
        var body = await ReadProblemAsync(response, cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task PostAnalysis_WithEmptyResumeId_ReturnsValidationProblemDetailsNotServerError()
    {
        using var cancellation = CreateCancellation();
        var (user, _) = await SeedUserAndResumeAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = Guid.Empty },
            cancellation.Token);
        var body = await ReadProblemAsync(response, cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task PostAnalysis_WithoutRequiredResumeId_ReturnsValidationDetails()
    {
        using var cancellation = CreateCancellation();
        var (user, _) = await SeedUserAndResumeAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        using var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/analyses",
            content,
            cancellation.Token);
        var body = await ReadProblemAsync(response, cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("ResumeId", out _));
    }

    [Fact]
    public async Task GetAnalysis_WithUnknownIdentifier_ReturnsNotFoundProblemDetails()
    {
        using var cancellation = CreateCancellation();
        var (user, _) = await SeedUserAndResumeAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.GetAsync(
            $"/api/analyses/{Id(9_100):D}",
            cancellation.Token);
        var body = await ReadProblemAsync(response, cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "Not Found",
            body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OwnershipFailure_DoesNotDiscloseInfrastructureOrForeignResourceData()
    {
        using var cancellation = CreateCancellation();
        var userA = CreateUser(1, "disclosure-a@example.com");
        var userB = CreateUser(2, "private-owner@example.com");
        var resumeB = CreateResume(
            20,
            userB.Id,
            "Confidential Foreign Resume Title");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(userA, userB, resumeB);
            await dbContext.SaveChangesAsync(cancellation.Token);
        }
        using var client = AuthenticatedClients.CreateForExistingUser(userA);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = resumeB.Id },
            cancellation.Token);
        var rawBody = await response.Content.ReadAsStringAsync(cancellation.Token);
        using var problem = JsonDocument.Parse(rawBody);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain(Fixture.ConnectionString, rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain(resumeB.Title, rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain(userB.Email, rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(resumeB.Id.ToString("D"), rawBody, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(User User, Resume Resume)>
        SeedUserAndResumeAsync(CancellationToken cancellationToken)
    {
        var user = CreateUser(1, "problems@example.com");
        var resume = CreateResume(10, user.Id);
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(user, resume);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (user, resume);
    }

    private static async Task<JsonDocument> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(body);
    }
}
