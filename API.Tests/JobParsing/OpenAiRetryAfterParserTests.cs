using System.Net;
using Api.Infrastructure.JobParsing.OpenAI;

namespace API.Tests.JobParsing;

public sealed class OpenAiRetryAfterParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(Now);

    [Fact]
    public void Parse_DeltaSeconds_ReturnsDelay()
    {
        using var response = Response("45");

        var result = OpenAiRetryAfterParser.Parse(
            response.Headers,
            _timeProvider);

        Assert.Equal(TimeSpan.FromSeconds(45), result);
    }

    [Fact]
    public void Parse_HttpDate_ReturnsDelayFromTimeProvider()
    {
        using var response = Response(Now.AddMinutes(2).ToString("r"));

        var result = OpenAiRetryAfterParser.Parse(
            response.Headers,
            _timeProvider);

        Assert.Equal(TimeSpan.FromMinutes(2), result);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Parse_InvalidOrNonPositiveValue_IsIgnored(string value)
    {
        using var response = Response(value);

        Assert.Null(OpenAiRetryAfterParser.Parse(
            response.Headers,
            _timeProvider));
    }

    [Fact]
    public void Parse_PastHttpDate_IsIgnored()
    {
        using var response = Response(Now.AddMinutes(-1).ToString("r"));

        Assert.Null(OpenAiRetryAfterParser.Parse(
            response.Headers,
            _timeProvider));
    }

    [Fact]
    public void Parse_ExcessiveDelay_IsCapped()
    {
        using var response = Response("999999999");

        Assert.Equal(
            OpenAiRetryAfterParser.MaximumAcceptedDelay,
            OpenAiRetryAfterParser.Parse(response.Headers, _timeProvider));
    }

    private static HttpResponseMessage Response(string retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
