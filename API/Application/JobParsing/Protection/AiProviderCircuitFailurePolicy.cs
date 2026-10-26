using Api.Application.JobParsing.Policies;

namespace Api.Application.JobParsing.Protection;

public static class AiProviderCircuitFailurePolicy
{
    public static bool IsQualifying(JobParseFailureClassification failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.FailureCode is
            JobParseFailureClassifier.ProviderTimeoutCode or
            JobParseFailureClassifier.ProviderUnavailableCode or
            JobParseFailureClassifier.ProviderRateLimitedCode;
    }
}
