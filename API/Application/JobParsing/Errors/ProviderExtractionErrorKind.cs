namespace Api.Application.JobParsing.Errors;

public enum ProviderExtractionErrorKind
{
    ProviderTimeout,
    ProviderUnavailable,
    ProviderRateLimited,
    ProviderConfigurationError,
    ProviderRefused,
    InvalidProviderResponse,
    SchemaValidationFailed
}
