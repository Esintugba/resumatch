using System.Text.Json;
using Api.Infrastructure.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Tests.ExceptionHandling;

public sealed class GlobalExceptionHandlerTests
{
    private const string SensitiveMessage =
        "Bad chunk size data near 'Authorization: Bearer secret-token'";
    private const string GenericDetail =
        "The request could not be processed because it was malformed or incomplete.";

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "Bad Request")]
    [InlineData(StatusCodes.Status408RequestTimeout, "Request Timeout")]
    [InlineData(StatusCodes.Status411LengthRequired, "Length Required")]
    [InlineData(StatusCodes.Status414UriTooLong, "URI Too Long")]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge, "Request Header Fields Too Large")]
    public async Task BadHttpRequestException_UsesItsOwnClientErrorStatus(
        int statusCode,
        string expectedTitle)
    {
        var (handled, context, body) = await HandleAsync(
            new BadHttpRequestException(SensitiveMessage, statusCode));

        Assert.True(handled);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(statusCode, body.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, body.GetProperty("title").GetString());
        Assert.Equal(GenericDetail, body.GetProperty("detail").GetString());
        Assert.Equal("/api/resumes", body.GetProperty("instance").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task BadHttpRequestException_DoesNotExposeExceptionMessage()
    {
        var (_, _, body) = await HandleAsync(
            new BadHttpRequestException(SensitiveMessage, StatusCodes.Status400BadRequest));

        var json = body.GetRawText();
        Assert.DoesNotContain("chunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadHttpRequestException_WithDefaultStatus_Returns400()
    {
        var (_, context, _) = await HandleAsync(new BadHttpRequestException(SensitiveMessage));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status302Found)]
    public async Task BadHttpRequestException_WithNonClientErrorStatus_IsNeverReportedAs5xx(
        int statusCode)
    {
        var (_, context, body) = await HandleAsync(
            new BadHttpRequestException(SensitiveMessage, statusCode));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PayloadTooLarge_KeepsSharedBodyLimitResponse()
    {
        var (handled, context, body) = await HandleAsync(
            new BadHttpRequestException(
                SensitiveMessage,
                StatusCodes.Status413PayloadTooLarge));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["title"] = "Payload Too Large",
                ["status"] = "413",
                ["detail"] = "The request body exceeds the permitted size.",
                ["instance"] = "/api/resumes"
            },
            body.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ToString()));
    }

    [Fact]
    public async Task UnknownException_StillReturns500()
    {
        var (_, context, _) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    private static async Task<(bool Handled, HttpContext Context, JsonElement Body)> HandleAsync(
        Exception exception)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/resumes";
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        return (handled, context, document.RootElement.Clone());
    }
}
