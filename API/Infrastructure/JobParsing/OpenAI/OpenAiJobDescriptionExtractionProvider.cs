using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Application.JobParsing.Models;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Infrastructure.JobParsing.OpenAI;

public sealed class OpenAiJobDescriptionExtractionProvider :
    IJobDescriptionExtractionProvider
{
    public const string ProviderName = "openai";
    public const string HttpClientName = "OpenAiJobDescriptionExtraction";

    private const int MaximumResponseEnvelopeBytes = 589_824;
    private const int MaximumErrorEnvelopeBytes = 16_384;

    private static readonly Uri ResponsesEndpoint =
        new("https://api.openai.com/v1/responses");
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly HashSet<string> TemporaryRateLimitCodes =
        new(StringComparer.Ordinal)
        {
            "slow_down",
            "rate_limit_exceeded"
        };

    private static readonly HashSet<string> QuotaFailureCodes =
        new(StringComparer.Ordinal)
        {
            "insufficient_quota",
            "credit_balance_exhausted",
            "organization_spend_limit_exceeded",
            "project_spend_limit_exceeded",
            "organization_usage_limit_exceeded"
        };

    private readonly HttpClient _httpClient;
    private readonly ProviderFacingResponseJsonValidator _responseValidator;
    private readonly ProviderExtractionPayloadBuilder _payloadBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxInputCharacters;
    private readonly int _maxOutputTokens;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _concurrencyGuard;
    private readonly ILogger<OpenAiJobDescriptionExtractionProvider> _logger;

    public OpenAiJobDescriptionExtractionProvider(
        HttpClient httpClient,
        IOptions<OpenAiJobDescriptionExtractionOptions> options,
        ProviderFacingResponseJsonValidator responseValidator,
        ProviderExtractionPayloadBuilder payloadBuilder,
        TimeProvider timeProvider,
        ILogger<OpenAiJobDescriptionExtractionProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _responseValidator = responseValidator ??
            throw new ArgumentNullException(nameof(responseValidator));
        _payloadBuilder = payloadBuilder ??
            throw new ArgumentNullException(nameof(payloadBuilder));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ??
            NullLogger<OpenAiJobDescriptionExtractionProvider>.Instance;
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value;
        if (configured.Provider != JobDescriptionExtractionProviderKind.OpenAI ||
            string.IsNullOrWhiteSpace(configured.ApiKey) ||
            string.IsNullOrWhiteSpace(configured.Model) ||
            configured.MaxInputCharacters <= 0 ||
            configured.MaxOutputTokens <= 0 ||
            configured.MaxConcurrentRequests <= 0 ||
            configured.RequestTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The OpenAI extraction provider configuration is invalid.");
        }

        _apiKey = configured.ApiKey;
        _model = configured.Model;
        _maxInputCharacters = configured.MaxInputCharacters;
        _maxOutputTokens = configured.MaxOutputTokens;
        _requestTimeout = configured.RequestTimeout;
        _concurrencyGuard = new SemaphoreSlim(
            configured.MaxConcurrentRequests,
            configured.MaxConcurrentRequests);
    }

    public async Task<ProviderExtractionResult> ExtractAsync(
        ProviderExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInputSize(request);

        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.provider.execute",
            ActivityKind.Client);
        activity?.SetTag("provider.name", ProviderName);
        activity?.SetTag("provider.model", _model);
        activity?.SetTag(
            "provider.prompt.version",
            OpenAiJobDescriptionExtractionPrompt.Version);

        await _concurrencyGuard.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "failed";
        JobPostingParseDraftMetrics.ProviderRequests.Add(
            1,
            new KeyValuePair<string, object?>("provider", ProviderName));
        _logger.LogInformation(
            "Job extraction provider request started. Provider={Provider} Model={Model}",
            ProviderName,
            _model);

        try
        {
            try
            {
                using var httpRequest = CreateRequest(request);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(_requestTimeout);

                using var response = await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    throw await MapHttpErrorAsync(response, timeout.Token);
                }

                var envelope = await ReadBoundedAsync(
                    response.Content,
                    MaximumResponseEnvelopeBytes,
                    timeout.Token);
                var result = ParseSuccessfulResponse(
                    response,
                    envelope,
                    request.Description);
                RecordUsage(result.Usage);
                outcome = "succeeded";
                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderTimeout);
            }
            catch (HttpRequestException)
            {
                throw new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderUnavailable);
            }
            catch (IOException)
            {
                throw new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderUnavailable);
            }
        }
        catch (ProviderExtractionException exception)
        {
            outcome = exception.ErrorKind.ToString();
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(
                "failure.code",
                exception.ErrorKind.ToString());
            if (exception.ErrorKind == ProviderExtractionErrorKind.ProviderTimeout)
            {
                JobPostingParseDraftMetrics.ProviderTimeouts.Add(1);
                _logger.LogWarning(
                    "Job extraction provider timed out. Provider={Provider} Model={Model}",
                    ProviderName,
                    _model);
            }
            else if (exception.ErrorKind ==
                ProviderExtractionErrorKind.ProviderRateLimited)
            {
                JobPostingParseDraftMetrics.ProviderRateLimited.Add(1);
                _logger.LogWarning(
                    "Job extraction provider rate limited the request. " +
                    "Provider={Provider} Model={Model} RetryAfterSeconds={RetryAfterSeconds}",
                    ProviderName,
                    _model,
                    exception.MinimumRetryDelay is null
                        ? null
                        : Math.Ceiling(
                            exception.MinimumRetryDelay.Value.TotalSeconds));
            }
            else if (exception.ErrorKind ==
                ProviderExtractionErrorKind.ProviderUnavailable)
            {
                JobPostingParseDraftMetrics.ProviderUnavailable.Add(1);
            }
            else if (exception.ErrorKind ==
                ProviderExtractionErrorKind.ProviderRefused)
            {
                JobPostingParseDraftMetrics.ProviderRefused.Add(1);
            }
            else if (exception.ErrorKind is
                ProviderExtractionErrorKind.InvalidProviderResponse or
                ProviderExtractionErrorKind.SchemaValidationFailed)
            {
                JobPostingParseDraftMetrics.ProviderInvalidResponses.Add(1);
            }

            throw;
        }
        finally
        {
            var elapsedMilliseconds =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            JobPostingParseDraftMetrics.ProviderLatency.Record(
                elapsedMilliseconds,
                new KeyValuePair<string, object?>("provider", ProviderName),
                new KeyValuePair<string, object?>("outcome", outcome));
            activity?.SetTag("provider.outcome", outcome);
            _logger.LogInformation(
                "Job extraction provider request completed. Provider={Provider} " +
                "Model={Model} Outcome={Outcome} ElapsedMilliseconds={ElapsedMilliseconds}",
                ProviderName,
                _model,
                outcome,
                elapsedMilliseconds);
            _concurrencyGuard.Release();
        }
    }

    private void RecordUsage(ProviderTokenUsage? usage)
    {
        if (usage is null)
        {
            _logger.LogDebug(
                "Job extraction provider usage metadata is unavailable. " +
                "Provider={Provider} Model={Model}",
                ProviderName,
                _model);
            return;
        }

        if (usage.InputTokens is { } inputTokens)
        {
            JobPostingParseDraftMetrics.ProviderInputTokens.Add(inputTokens);
        }

        if (usage.OutputTokens is { } outputTokens)
        {
            JobPostingParseDraftMetrics.ProviderOutputTokens.Add(outputTokens);
        }

        if (usage.TotalTokens is { } totalTokens)
        {
            JobPostingParseDraftMetrics.ProviderTotalTokens.Add(totalTokens);
        }

        _logger.LogDebug(
            "Job extraction provider usage metadata recorded. " +
            "Provider={Provider} Model={Model} InputTokens={InputTokens} " +
            "OutputTokens={OutputTokens} TotalTokens={TotalTokens}",
            ProviderName,
            _model,
            usage.InputTokens,
            usage.OutputTokens,
            usage.TotalTokens);
    }

    private void ValidateInputSize(ProviderExtractionRequest request)
    {
        var length = (long)request.Title.Length + request.Description.Length;
        if (length > _maxInputCharacters)
        {
            throw new ProviderExtractionException(
                ProviderExtractionErrorKind.SchemaValidationFailed);
        }
    }

    private HttpRequestMessage CreateRequest(ProviderExtractionRequest request)
    {
        var sourceData = JsonSerializer.Serialize(
            new SourceData(request.Title, request.Description),
            SerializerOptions);
        var body = new ResponseRequest(
            _model,
            OpenAiJobDescriptionExtractionPrompt.Instructions,
            [
                new InputMessage(
                    "user",
                    [new InputContent("input_text", sourceData)])
            ],
            new TextConfiguration(
                new TextFormat(
                    "json_schema",
                    "job_skill_extraction",
                    true,
                    OpenAiStructuredOutputSchema.GetSchema())),
            false,
            _maxOutputTokens,
            []);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, SerializerOptions);

        var message = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
        {
            Content = new ByteArrayContent(bytes)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json")
        {
            CharSet = "utf-8"
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _apiKey);
        return message;
    }

    private ProviderExtractionResult ParseSuccessfulResponse(
        HttpResponseMessage response,
        byte[] envelope,
        string originalDescription)
    {
        try
        {
            using var document = JsonDocument.Parse(
                envelope,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(root, "status", out var status) ||
                !string.Equals(status, "completed", StringComparison.Ordinal) ||
                HasIncompleteDetails(root) ||
                HasResponseError(root))
            {
                throw InvalidResponse();
            }

            var outputTexts = new List<string>();
            var hasRefusal = false;
            if (!root.TryGetProperty("output", out var output) ||
                output.ValueKind != JsonValueKind.Array ||
                output.GetArrayLength() != 1)
            {
                throw InvalidResponse();
            }

            foreach (var outputItem in output.EnumerateArray())
            {
                if (outputItem.ValueKind != JsonValueKind.Object ||
                    !TryGetRequiredString(outputItem, "type", out var outputType) ||
                    !string.Equals(outputType, "message", StringComparison.Ordinal) ||
                    !outputItem.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array ||
                    content.GetArrayLength() != 1)
                {
                    throw InvalidResponse();
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.ValueKind != JsonValueKind.Object ||
                        !TryGetRequiredString(contentItem, "type", out var contentType))
                    {
                        throw InvalidResponse();
                    }

                    if (string.Equals(contentType, "refusal", StringComparison.Ordinal))
                    {
                        hasRefusal = true;
                        continue;
                    }

                    if (!string.Equals(contentType, "output_text", StringComparison.Ordinal) ||
                        !TryGetRequiredString(contentItem, "text", out var text))
                    {
                        throw InvalidResponse();
                    }

                    outputTexts.Add(text);
                }
            }

            if (hasRefusal)
            {
                throw new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderRefused);
            }

            if (outputTexts.Count != 1)
            {
                throw InvalidResponse();
            }

            var providerFacing = _responseValidator.Validate(outputTexts[0]);
            var internalPayload = _payloadBuilder.Build(
                providerFacing,
                originalDescription);
            var requestId = GetRequestId(response, root);
            var model = TryGetRequiredString(root, "model", out var actualModel)
                ? actualModel
                : _model;

            if (requestId.Length > 200 || model.Length > 200)
            {
                throw InvalidResponse();
            }

            return new ProviderExtractionResult(
                requestId,
                ProviderName,
                model,
                internalPayload,
                TryReadUsage(root));
        }
        catch (ProviderExtractionException)
        {
            throw;
        }
        catch (ParserValidationException exception)
        {
            throw new ProviderExtractionException(
                exception.Code is "empty_provider_response" or
                    "provider_response_too_large" or
                    "markdown_response_not_allowed" or
                    "invalid_provider_json"
                    ? ProviderExtractionErrorKind.InvalidProviderResponse
                    : ProviderExtractionErrorKind.SchemaValidationFailed);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    private static ProviderTokenUsage? TryReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var inputTokens = TryReadNonNegativeInt64(usage, "input_tokens");
        var outputTokens = TryReadNonNegativeInt64(usage, "output_tokens");
        var totalTokens = TryReadNonNegativeInt64(usage, "total_tokens");
        return inputTokens is null && outputTokens is null && totalTokens is null
            ? null
            : new ProviderTokenUsage(inputTokens, outputTokens, totalTokens);
    }

    private static long? TryReadNonNegativeInt64(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var parsed) &&
            parsed >= 0
                ? parsed
                : null;
    }

    private async Task<ProviderExtractionException> MapHttpErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var retryAfter = OpenAiRetryAfterParser.Parse(
            response.Headers,
            _timeProvider);
        var error = await TryReadErrorAsync(response.Content, cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            return new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderTimeout,
                retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (IsQuotaFailure(error))
            {
                return new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderConfigurationError);
            }

            if (error.Code is not null &&
                TemporaryRateLimitCodes.Contains(error.Code))
            {
                return new ProviderExtractionException(
                    ProviderExtractionErrorKind.ProviderRateLimited,
                    retryAfter);
            }

            return new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderConfigurationError);
        }

        if (response.StatusCode is HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout)
        {
            return new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderUnavailable,
                retryAfter);
        }

        return new ProviderExtractionException(
            ProviderExtractionErrorKind.ProviderConfigurationError);
    }

    private static bool IsQuotaFailure(ProviderError error) =>
        error.Code is not null && QuotaFailureCodes.Contains(error.Code) ||
        string.Equals(error.Type, "insufficient_quota", StringComparison.Ordinal);

    private static async Task<ProviderError> TryReadErrorAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await ReadBoundedAsync(
                content,
                MaximumErrorEnvelopeBytes,
                cancellationToken);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = 8 });
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return ProviderError.Unknown;
            }

            return new ProviderError(
                GetOptionalSafeString(error, "type"),
                GetOptionalSafeString(error, "code"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ProviderError.Unknown;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 16_384));
        var rented = ArrayPool<byte>.Shared.Rent(8_192);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(
                    rented.AsMemory(0, rented.Length),
                    cancellationToken);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > maximumBytes)
                {
                    throw InvalidResponse();
                }

                buffer.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string GetRequestId(
        HttpResponseMessage response,
        JsonElement root)
    {
        if (response.Headers.TryGetValues("x-request-id", out var values))
        {
            var requestIds = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            if (requestIds.Length == 1)
            {
                return requestIds[0];
            }
        }

        if (TryGetRequiredString(root, "id", out var responseId))
        {
            return responseId;
        }

        throw InvalidResponse();
    }

    private static bool HasIncompleteDetails(JsonElement root) =>
        root.TryGetProperty("incomplete_details", out var details) &&
        details.ValueKind != JsonValueKind.Null;

    private static bool HasResponseError(JsonElement root) =>
        root.TryGetProperty("error", out var error) &&
        error.ValueKind != JsonValueKind.Null;

    private static bool TryGetRequiredString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static string? GetOptionalSafeString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) || value.Length > 100
            ? null
            : value;
    }

    private static ProviderExtractionException InvalidResponse() =>
        new(ProviderExtractionErrorKind.InvalidProviderResponse);

    private sealed record SourceData(string Title, string Description);

    private sealed record ResponseRequest(
        string Model,
        string Instructions,
        IReadOnlyCollection<InputMessage> Input,
        TextConfiguration Text,
        bool Store,
        int MaxOutputTokens,
        IReadOnlyCollection<object> Tools);

    private sealed record InputMessage(
        string Role,
        IReadOnlyCollection<InputContent> Content);

    private sealed record InputContent(string Type, string Text);

    private sealed record TextConfiguration(TextFormat Format);

    private sealed record TextFormat(
        string Type,
        string Name,
        bool Strict,
        JsonElement Schema);

    private sealed record ProviderError(string? Type, string? Code)
    {
        public static ProviderError Unknown { get; } = new(null, null);
    }
}
