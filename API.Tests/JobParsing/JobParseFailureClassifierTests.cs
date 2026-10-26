using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Application.JobParsing.Policies;

namespace API.Tests.JobParsing;

public sealed class JobParseFailureClassifierTests
{
    [Fact]
    public void TryClassify_Timeout_IsRetryable()
    {
        var result = Classify(new TimeoutException("sensitive timeout detail"));

        Assert.Equal("provider_timeout", result.FailureCode);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_ProviderUnavailable_IsRetryable()
    {
        var result = Classify(new HttpRequestException("sensitive endpoint"));

        Assert.Equal("provider_unavailable", result.FailureCode);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData(
        ProviderExtractionErrorKind.ProviderTimeout,
        "provider_timeout",
        true)]
    [InlineData(
        ProviderExtractionErrorKind.ProviderUnavailable,
        "provider_unavailable",
        true)]
    [InlineData(
        ProviderExtractionErrorKind.ProviderRateLimited,
        "provider_rate_limited",
        true)]
    [InlineData(
        ProviderExtractionErrorKind.ProviderConfigurationError,
        "provider_configuration_error",
        false)]
    [InlineData(
        ProviderExtractionErrorKind.ProviderRefused,
        "provider_refused",
        false)]
    [InlineData(
        ProviderExtractionErrorKind.InvalidProviderResponse,
        "invalid_provider_response",
        false)]
    [InlineData(
        ProviderExtractionErrorKind.SchemaValidationFailed,
        "schema_validation_failed",
        false)]
    public void TryClassify_TypedProviderError_UsesSafePolicy(
        ProviderExtractionErrorKind kind,
        string expectedCode,
        bool expectedRetryable)
    {
        var result = Classify(new ProviderExtractionException(kind));

        Assert.Equal(expectedCode, result.FailureCode);
        Assert.Equal(expectedRetryable, result.IsRetryable);
    }

    [Fact]
    public void TryClassify_RateLimit_PreservesMinimumRetryDelay()
    {
        var minimumDelay = TimeSpan.FromSeconds(45);

        var result = Classify(new ProviderExtractionException(
            ProviderExtractionErrorKind.ProviderRateLimited,
            minimumDelay));

        Assert.True(result.IsRetryable);
        Assert.Equal(minimumDelay, result.MinimumRetryDelay);
    }

    [Fact]
    public void ProviderError_NegativeMinimumDelay_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderRateLimited,
                TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ProviderError_NonRetryableKindCannotCarryRetryDelay()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderConfigurationError,
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ProviderError_MessageDoesNotContainRawProviderContent()
    {
        const string rawContent = "raw provider payload with secret key";
        var exception = new ProviderExtractionException(
            ProviderExtractionErrorKind.InvalidProviderResponse);

        Assert.DoesNotContain(
            rawContent,
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            "invalid_provider_response",
            exception.SafeFailureCode);
    }

    [Fact]
    public void TryClassify_InvalidJson_IsNotRetryable()
    {
        var result = Classify(new ParserValidationException(
            "invalid_provider_json",
            "raw payload detail"));

        Assert.Equal("invalid_provider_response", result.FailureCode);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_SchemaViolation_IsNotRetryable()
    {
        var result = Classify(new ParserValidationException(
            "missing_provider_property",
            "raw schema detail"));

        Assert.Equal("schema_validation_failed", result.FailureCode);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_StaleSource_IsNotRetryable()
    {
        var result = Classify(new ParserValidationException(
            "description_hash_mismatch",
            "source content"));

        Assert.Equal("stale_source", result.FailureCode);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_UnknownException_IsConservativelyNotRetryable()
    {
        var result = Classify(new InvalidOperationException("secret detail"));

        Assert.Equal("unexpected_failure", result.FailureCode);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_CallerCancellation_IsNotAProviderFailure()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var classified = JobParseFailureClassifier.TryClassify(
            new OperationCanceledException(source.Token),
            source.Token,
            out var result);

        Assert.False(classified);
        Assert.Null(result);
    }

    [Fact]
    public void TryClassify_NonCallerCancellation_IsTreatedAsProviderTimeout()
    {
        var result = Classify(new TaskCanceledException("provider timeout"));

        Assert.Equal("provider_timeout", result.FailureCode);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void TryClassify_ResultNeverContainsRawExceptionMessage()
    {
        const string sensitiveMessage = "secret provider response and API key";

        var classifications = new[]
        {
            Classify(new TimeoutException(sensitiveMessage)),
            Classify(new HttpRequestException(sensitiveMessage)),
            Classify(new ParserValidationException(
                "invalid_provider_json",
                sensitiveMessage)),
            Classify(new InvalidOperationException(sensitiveMessage))
        };

        Assert.All(classifications, result =>
            Assert.DoesNotContain(
                sensitiveMessage,
                result.FailureCode,
                StringComparison.Ordinal));
    }

    [Fact]
    public void RetryExhausted_IsSafeAndNotRetryable()
    {
        Assert.Equal(
            "retry_exhausted",
            JobParseFailureClassifier.RetryExhausted.FailureCode);
        Assert.False(JobParseFailureClassifier.RetryExhausted.IsRetryable);
    }

    private static JobParseFailureClassification Classify(Exception exception)
    {
        var classified = JobParseFailureClassifier.TryClassify(
            exception,
            CancellationToken.None,
            out var result);

        Assert.True(classified);
        return Assert.IsType<JobParseFailureClassification>(result);
    }
}
