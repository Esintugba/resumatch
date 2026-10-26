namespace Api.Application.JobParsing.Errors;

public static class ProviderExtractionFailureCodes
{
    public const string ProviderTimeout = "provider_timeout";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderRateLimited = "provider_rate_limited";
    public const string ProviderConfigurationError = "provider_configuration_error";
    public const string ProviderRefused = "provider_refused";
    public const string InvalidProviderResponse = "invalid_provider_response";
    public const string SchemaValidationFailed = "schema_validation_failed";
}
