using System.Collections.Concurrent;
using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Api.Application.JobParsing.Errors;
using Api.Application.JobParsing.Models;
using Api.Infrastructure.JobParsing.OpenAI;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class OpenAiJobDescriptionExtractionProviderTests
{
    private const string ApiKey = "test-secret-api-key";
    private const string Model = "test-structured-output-model";
    private const string Description = "C# and Docker are required.";

    [Fact]
    public async Task ExtractAsync_EmitsPromptVersionWithoutPromptContent()
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == "ResuMatch.JobParsing",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "job_parser.provider.execute")
                {
                    captured = activity;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        var provider = CreateProvider(new RecordingHandler((_, _) => Task.FromResult(
            SuccessResponse(ProviderJson(Candidate("C#", "required", "C#"))))));

        await provider.ExtractAsync(Request(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(
            OpenAiJobDescriptionExtractionPrompt.Version,
            captured.GetTagItem("provider.prompt.version"));
        var tags = string.Join(' ', captured.TagObjects.Select(tag => tag.Value));
        Assert.DoesNotContain(
            OpenAiJobDescriptionExtractionPrompt.Instructions,
            tags,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Description, tags, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_SendsSecureResponsesRequest_AndBuildsInternalPayload()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            SuccessResponse(ProviderJson(
                Candidate("C#", "required", "C#"),
                Candidate("Docker", "required", "Docker")))));
        var provider = CreateProvider(handler);

        var result = await provider.ExtractAsync(Request(), CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.openai.com/v1/responses", handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(ApiKey, handler.AuthorizationParameter);
        Assert.Equal("openai-request-1", result.ProviderRequestId);
        Assert.Equal(OpenAiJobDescriptionExtractionProvider.ProviderName,
            result.ProviderName);
        Assert.Equal(Model, result.ModelName);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var root = request.RootElement;
        Assert.Equal(Model, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(2048, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Empty(root.GetProperty("tools").EnumerateArray());
        Assert.Contains(
            "untrusted data",
            root.GetProperty("instructions").GetString(),
            StringComparison.Ordinal);

        var sourceText = root.GetProperty("input")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
        using var source = JsonDocument.Parse(sourceText);
        Assert.Equal("Backend Engineer",
            source.RootElement.GetProperty("title").GetString());
        Assert.Equal(Description,
            source.RootElement.GetProperty("description").GetString());
        Assert.DoesNotContain("skillCatalog", sourceText, StringComparison.Ordinal);

        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(
            "skills",
            schema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));
        var candidateSchema = schema.GetProperty("properties")
            .GetProperty("skills")
            .GetProperty("items");
        Assert.False(candidateSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "name", "requirement", "confidence", "evidence" },
            candidateSchema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            new[] { "number", "null" },
            candidateSchema.GetProperty("properties")
                .GetProperty("confidence")
                .GetProperty("anyOf")
                .EnumerateArray()
                .Select(item => item.GetProperty("type").GetString()));
        var evidenceProperties = schema.GetProperty("properties")
            .GetProperty("skills")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("evidence")
            .GetProperty("items")
            .GetProperty("properties");
        Assert.True(evidenceProperties.TryGetProperty("text", out _));
        Assert.False(evidenceProperties.TryGetProperty("start", out _));
        Assert.False(evidenceProperties.TryGetProperty("length", out _));

        using var internalJson = JsonDocument.Parse(result.RawJsonPayload);
        var skills = internalJson.RootElement.GetProperty("skills");
        Assert.Equal(0, skills[0].GetProperty("evidence")[0]
            .GetProperty("start").GetInt32());
        Assert.Equal(7, skills[1].GetProperty("evidence")[0]
            .GetProperty("start").GetInt32());
    }

    [Fact]
    public async Task ExtractAsync_Refusal_IsTerminal()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(EnvelopeResponse(
                "completed",
                [Message(new { type = "refusal", refusal = "declined" })]))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderRefused,
            exception.ErrorKind);
        Assert.False(exception.IsRetryable);
    }

    [Fact]
    public async Task ExtractAsync_IncompleteResponse_IsTerminal()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(EnvelopeResponse(
                "incomplete",
                [Message(OutputText("{}"))],
                new { reason = "max_output_tokens" }))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.InvalidProviderResponse,
            exception.ErrorKind);
    }

    [Fact]
    public async Task ExtractAsync_MultipleStructuredOutputs_AreRejected()
    {
        var json = ProviderJson(Candidate("C#", "required", "C#"));
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(EnvelopeResponse(
                "completed",
                [Message(OutputText(json), OutputText(json))]))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.InvalidProviderResponse,
            exception.ErrorKind);
    }

    [Fact]
    public async Task ExtractAsync_InvalidStructuredJson_IsRejected()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(SuccessResponse("not-json"))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.InvalidProviderResponse,
            exception.ErrorKind);
    }

    [Fact]
    public async Task ExtractAsync_EvidenceMissingFromSource_IsRejected()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(SuccessResponse(ProviderJson(
                Candidate("Kubernetes", "required", "Kubernetes"))))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.SchemaValidationFailed,
            exception.ErrorKind);
    }

    [Fact]
    public async Task ExtractAsync_InputOverConfiguredLimit_IsRejectedBeforeHttp()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(SuccessResponse(ProviderJson())));
        var provider = CreateProvider(handler, maxInputCharacters: 5);

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.SchemaValidationFailed,
            exception.ErrorKind);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_Temporary429_IsRetryableAndCarriesDeltaRetryAfter()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(ErrorResponse(
                HttpStatusCode.TooManyRequests,
                "rate_limit_error",
                "slow_down",
                "45"))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderRateLimited,
            exception.ErrorKind);
        Assert.True(exception.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.MinimumRetryDelay);
    }

    [Fact]
    public async Task ExtractAsync_Http408_IsTypedProviderTimeout()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(ErrorResponse(
                HttpStatusCode.RequestTimeout,
                "timeout_error",
                "request_timeout"))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderTimeout,
            exception.ErrorKind);
        Assert.True(exception.IsRetryable);
    }

    [Theory]
    [InlineData("insufficient_quota")]
    [InlineData("credit_balance_exhausted")]
    [InlineData("organization_spend_limit_exceeded")]
    [InlineData("unknown_429")]
    public async Task ExtractAsync_QuotaOrUnknown429_IsTerminal(string code)
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(ErrorResponse(
                HttpStatusCode.TooManyRequests,
                code == "insufficient_quota" ? "insufficient_quota" : "rate_limit_error",
                code))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderConfigurationError,
            exception.ErrorKind);
        Assert.False(exception.IsRetryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ExtractAsync_TransientServerError_IsRetryable(
        HttpStatusCode statusCode)
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(ErrorResponse(
                statusCode,
                "service_unavailable_error",
                "server_is_overloaded"))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderUnavailable,
            exception.ErrorKind);
        Assert.True(exception.IsRetryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ExtractAsync_ConfigurationHttpError_IsTerminal(
        HttpStatusCode statusCode)
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(ErrorResponse(
                statusCode,
                "invalid_request_error",
                "invalid_api_key"))));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderConfigurationError,
            exception.ErrorKind);
        Assert.False(exception.IsRetryable);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_CallerCancellation_IsPropagated()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var provider = CreateProvider(handler, requestTimeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();

        var execution = provider.ExtractAsync(Request(), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_RequestTimeout_IsRetryableAndSendsOneRequest()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var provider = CreateProvider(handler, requestTimeout: TimeSpan.FromMilliseconds(25));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderTimeout,
            exception.ErrorKind);
        Assert.True(exception.IsRetryable);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_OversizedEnvelope_IsRejectedWithoutUnboundedRead()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', 600_000))
            })));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.InvalidProviderResponse,
            exception.ErrorKind);
    }

    [Fact]
    public async Task ExtractAsync_HttpTransportFailure_DoesNotExposeSecret()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            throw new HttpRequestException($"transport failure {ApiKey}")));

        var exception = await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        Assert.Equal(ProviderExtractionErrorKind.ProviderUnavailable,
            exception.ErrorKind);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_TransportFailure_EmitsBoundedUnavailableMetric()
    {
        // The meter is process-wide and test classes run in parallel, so only
        // measurements from this test's async flow are recorded.
        var currentTest = new AsyncLocal<bool> { Value = true };
        var measurements = new ConcurrentQueue<(long Value, string[] Keys)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "ResuMatch.JobParsing" &&
                instrument.Name == "resumatch.job_parsing.provider.unavailable")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (currentTest.Value)
            {
                measurements.Enqueue((
                    value,
                    tags.ToArray().Select(item => item.Key).ToArray()));
            }
        });
        listener.Start();
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            throw new HttpRequestException("transport failure")));

        await Assert.ThrowsAsync<ProviderExtractionException>(() =>
            provider.ExtractAsync(Request(), CancellationToken.None));

        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Empty(measurement.Keys);
    }

    [Fact]
    public async Task ExtractAsync_OptionalUsage_IsReturnedAndEmittedWithoutSensitiveLabels()
    {
        // The meter is process-wide and test classes run in parallel, so only
        // measurements from this test's async flow are recorded.
        var currentTest = new AsyncLocal<bool> { Value = true };
        var measurements = new ConcurrentQueue<(string Name, long Value, string[] Keys)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "ResuMatch.JobParsing")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (currentTest.Value)
            {
                measurements.Enqueue((
                    instrument.Name,
                    value,
                    tags.ToArray().Select(tag => tag.Key).ToArray()));
            }
        });
        listener.Start();
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(SuccessResponse(
                ProviderJson(Candidate("C#", "required", "C#")),
                new { input_tokens = 100, output_tokens = 25, total_tokens = 125 }))));

        var result = await provider.ExtractAsync(Request(), CancellationToken.None);

        Assert.Equal(new ProviderTokenUsage(100, 25, 125), result.Usage);
        Assert.Contains(measurements, item =>
            item.Name == "resumatch.job_parsing.provider.input_tokens" &&
            item.Value == 100);
        Assert.Contains(measurements, item =>
            item.Name == "resumatch.job_parsing.provider.output_tokens" &&
            item.Value == 25);
        Assert.Contains(measurements, item =>
            item.Name == "resumatch.job_parsing.provider.total_tokens" &&
            item.Value == 125);
        Assert.All(measurements.SelectMany(item => item.Keys), key =>
        {
            Assert.DoesNotContain("user", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("job", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("draft", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("description", key, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ExtractAsync_MissingOrInvalidUsage_DoesNotFailExtraction()
    {
        var provider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromResult(SuccessResponse(
                ProviderJson(),
                new { input_tokens = -1, output_tokens = "invalid" }))));

        var result = await provider.ExtractAsync(Request(), CancellationToken.None);

        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ExtractAsync_ProcessLocalConcurrencyGuard_BoundsOutboundRequests()
    {
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            firstEntered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref active);
            return SuccessResponse(ProviderJson());
        });
        var provider = CreateProvider(handler, maxConcurrentRequests: 1);

        var first = provider.ExtractAsync(Request(), CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = provider.ExtractAsync(Request(), CancellationToken.None);
        await Task.Delay(50);
        Assert.Equal(1, handler.CallCount);
        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(1, maximumActive);
    }

    private static OpenAiJobDescriptionExtractionProvider CreateProvider(
        HttpMessageHandler handler,
        TimeSpan? requestTimeout = null,
        int maxInputCharacters = 10_000,
        int maxConcurrentRequests = 1)
    {
        var options = Options.Create(new OpenAiJobDescriptionExtractionOptions
        {
            Provider = JobDescriptionExtractionProviderKind.OpenAI,
            ApiKey = ApiKey,
            Model = Model,
            MaxInputCharacters = maxInputCharacters,
            MaxOutputTokens = 2_048,
            MaxConcurrentRequests = maxConcurrentRequests,
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2)
        });
        return new OpenAiJobDescriptionExtractionProvider(
            new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            options,
            new ProviderFacingResponseJsonValidator(),
            new ProviderExtractionPayloadBuilder(),
            TimeProvider.System);
    }

    private static ProviderExtractionRequest Request() =>
        new("Backend Engineer", Description, "job-parser-v1");

    private static HttpResponseMessage SuccessResponse(
        string providerJson,
        object? usage = null)
    {
        var response = EnvelopeResponse(
            "completed",
            [Message(OutputText(providerJson))],
            usage: usage);
        response.Headers.TryAddWithoutValidation("x-request-id", "openai-request-1");
        return response;
    }

    private static HttpResponseMessage EnvelopeResponse(
        string status,
        object[] output,
        object? incompleteDetails = null,
        object? usage = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "response-1",
            model = Model,
            status,
            incomplete_details = incompleteDetails,
            usage,
            output
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage ErrorResponse(
        HttpStatusCode statusCode,
        string type,
        string code,
        string? retryAfter = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                error = new
                {
                    type,
                    code,
                    message = $"provider detail {ApiKey}"
                }
            }))
        };
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }

    private static object Message(params object[] content) => new
    {
        type = "message",
        role = "assistant",
        status = "completed",
        content
    };

    private static object OutputText(string text) => new
    {
        type = "output_text",
        text
    };

    private static string ProviderJson(params object[] skills) =>
        JsonSerializer.Serialize(new { skills });

    private static object Candidate(
        string name,
        string requirement,
        string evidence) => new
    {
        name,
        requirement,
        confidence = 0.9m,
        evidence = new[] { new { text = evidence } }
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) :
        HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri?.AbsoluteUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Started.TrySetResult();
            return await send(request, cancellationToken);
        }
    }
}
