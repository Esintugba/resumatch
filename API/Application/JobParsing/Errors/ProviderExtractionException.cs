namespace Api.Application.JobParsing.Errors;

public sealed class ProviderExtractionException : Exception
{
    public ProviderExtractionException(
        ProviderExtractionErrorKind errorKind,
        TimeSpan? minimumRetryDelay = null)
        : base(GetSafeMessage(errorKind))
    {
        if (!Enum.IsDefined(errorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(errorKind));
        }

        if (minimumRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRetryDelay),
                "Minimum retry delay cannot be negative.");
        }

        ErrorKind = errorKind;
        IsRetryable = IsRetryableKind(errorKind);
        if (!IsRetryable && minimumRetryDelay.HasValue)
        {
            throw new ArgumentException(
                "A non-retryable provider error cannot include a retry delay.",
                nameof(minimumRetryDelay));
        }

        MinimumRetryDelay = minimumRetryDelay.HasValue &&
            minimumRetryDelay.Value > TimeSpan.Zero
            ? minimumRetryDelay
            : null;
        SafeFailureCode = GetFailureCode(errorKind);
    }

    public ProviderExtractionErrorKind ErrorKind { get; }
    public string SafeFailureCode { get; }
    public bool IsRetryable { get; }
    public TimeSpan? MinimumRetryDelay { get; }

    private static bool IsRetryableKind(ProviderExtractionErrorKind errorKind) =>
        errorKind is ProviderExtractionErrorKind.ProviderTimeout or
            ProviderExtractionErrorKind.ProviderUnavailable or
            ProviderExtractionErrorKind.ProviderRateLimited;

    private static string GetFailureCode(ProviderExtractionErrorKind errorKind) =>
        errorKind switch
        {
            ProviderExtractionErrorKind.ProviderTimeout =>
                ProviderExtractionFailureCodes.ProviderTimeout,
            ProviderExtractionErrorKind.ProviderUnavailable =>
                ProviderExtractionFailureCodes.ProviderUnavailable,
            ProviderExtractionErrorKind.ProviderRateLimited =>
                ProviderExtractionFailureCodes.ProviderRateLimited,
            ProviderExtractionErrorKind.ProviderConfigurationError =>
                ProviderExtractionFailureCodes.ProviderConfigurationError,
            ProviderExtractionErrorKind.ProviderRefused =>
                ProviderExtractionFailureCodes.ProviderRefused,
            ProviderExtractionErrorKind.InvalidProviderResponse =>
                ProviderExtractionFailureCodes.InvalidProviderResponse,
            ProviderExtractionErrorKind.SchemaValidationFailed =>
                ProviderExtractionFailureCodes.SchemaValidationFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(errorKind))
        };

    private static string GetSafeMessage(ProviderExtractionErrorKind errorKind) =>
        Enum.IsDefined(errorKind)
            ? "The job description extraction provider request failed."
            : throw new ArgumentOutOfRangeException(nameof(errorKind));
}
