using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;

namespace Api.Application.JobParsing.Policies;

public sealed record JobParseFailureClassification
{
    public JobParseFailureClassification(
        string failureCode,
        bool isRetryable,
        TimeSpan? minimumRetryDelay = null)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException(
                "Failure code is required.",
                nameof(failureCode));
        }

        if (minimumRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRetryDelay),
                "Minimum retry delay cannot be negative.");
        }

        if (!isRetryable && minimumRetryDelay.HasValue)
        {
            throw new ArgumentException(
                "A non-retryable failure cannot include a retry delay.",
                nameof(minimumRetryDelay));
        }

        FailureCode = failureCode;
        IsRetryable = isRetryable;
        MinimumRetryDelay = minimumRetryDelay.HasValue &&
            minimumRetryDelay.Value > TimeSpan.Zero
            ? minimumRetryDelay
            : null;
    }

    public string FailureCode { get; }
    public bool IsRetryable { get; }
    public TimeSpan? MinimumRetryDelay { get; }
}

public static class JobParseFailureClassifier
{
    public const string ProviderTimeoutCode =
        ProviderExtractionFailureCodes.ProviderTimeout;
    public const string ProviderUnavailableCode =
        ProviderExtractionFailureCodes.ProviderUnavailable;
    public const string ProviderRateLimitedCode =
        ProviderExtractionFailureCodes.ProviderRateLimited;
    public const string ProviderConfigurationErrorCode =
        ProviderExtractionFailureCodes.ProviderConfigurationError;
    public const string ProviderRefusedCode =
        ProviderExtractionFailureCodes.ProviderRefused;
    public const string InvalidProviderResponseCode =
        ProviderExtractionFailureCodes.InvalidProviderResponse;
    public const string SchemaValidationFailedCode =
        ProviderExtractionFailureCodes.SchemaValidationFailed;
    public const string StaleSourceCode = "stale_source";
    public const string RetryExhaustedCode = "retry_exhausted";
    public const string UnexpectedFailureCode = "unexpected_failure";

    public static JobParseFailureClassification RetryExhausted { get; } =
        new(RetryExhaustedCode, false);

    public static bool TryClassify(
        Exception exception,
        CancellationToken cancellationToken,
        out JobParseFailureClassification? classification)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException &&
            cancellationToken.IsCancellationRequested)
        {
            classification = null;
            return false;
        }

        classification = exception switch
        {
            ProviderExtractionException providerException => new(
                providerException.SafeFailureCode,
                providerException.IsRetryable,
                providerException.MinimumRetryDelay),
            TimeoutException => new(ProviderTimeoutCode, true),
            OperationCanceledException => new(ProviderTimeoutCode, true),
            HttpRequestException => new(ProviderUnavailableCode, true),
            ParserValidationException parserException =>
                ClassifyParserValidation(parserException.Code),
            _ => new(UnexpectedFailureCode, false)
        };

        return true;
    }

    private static JobParseFailureClassification ClassifyParserValidation(
        string parserCode)
    {
        return parserCode switch
        {
            "description_hash_mismatch" or
            "source_input_hash_mismatch" => new(StaleSourceCode, false),

            "empty_provider_response" or
            "missing_provider_response" or
            "provider_response_too_large" or
            "markdown_response_not_allowed" or
            "invalid_provider_json" => new(InvalidProviderResponseCode, false),

            _ => new(SchemaValidationFailedCode, false)
        };
    }
}
