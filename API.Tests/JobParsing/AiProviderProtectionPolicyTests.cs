using Api.Application.JobParsing;
using Api.Application.JobParsing.Policies;
using Api.Application.JobParsing.Protection;

namespace API.Tests.JobParsing;

public sealed class AiProviderProtectionPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void OptionsValidator_RejectsInvalidDistributedConcurrency(int value)
    {
        var result = new JobPostingParseDraftOptionsValidator().Validate(
            null, ValidOptions(distributedLimit: value));
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void OptionsValidator_RejectsInvalidCircuitThreshold(int value)
    {
        var result = new JobPostingParseDraftOptionsValidator().Validate(
            null, ValidOptions(threshold: value));
        Assert.True(result.Failed);
    }

    [Fact]
    public void OptionsValidator_RejectsInvalidOpenDuration()
    {
        var result = new JobPostingParseDraftOptionsValidator().Validate(
            null, ValidOptions(openDuration: TimeSpan.FromHours(2)));
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void OptionsValidator_RejectsInvalidHalfOpenProbeCount(int value)
    {
        var result = new JobPostingParseDraftOptionsValidator().Validate(
            null, ValidOptions(halfOpenProbes: value));
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(JobParseFailureClassifier.ProviderTimeoutCode, true)]
    [InlineData(JobParseFailureClassifier.ProviderUnavailableCode, true)]
    [InlineData(JobParseFailureClassifier.ProviderRateLimitedCode, true)]
    [InlineData(JobParseFailureClassifier.InvalidProviderResponseCode, false)]
    [InlineData(JobParseFailureClassifier.ProviderConfigurationErrorCode, false)]
    [InlineData(JobParseFailureClassifier.ProviderRefusedCode, false)]
    [InlineData(JobParseFailureClassifier.StaleSourceCode, false)]
    public void CircuitFailurePolicy_ClassifiesOnlyTransientProviderFailures(
        string code,
        bool expected)
    {
        Assert.Equal(expected, AiProviderCircuitFailurePolicy.IsQualifying(
            new JobParseFailureClassification(code, expected)));
    }

    private static JobPostingParseDraftOptions ValidOptions(
        int distributedLimit = 1,
        int threshold = 3,
        TimeSpan? openDuration = null,
        int halfOpenProbes = 1) => new()
    {
        LeaseDuration = TimeSpan.FromMinutes(5),
        ProviderTimeout = TimeSpan.FromSeconds(30),
        FinalizationSafetyMargin = TimeSpan.FromSeconds(15),
        DistributedMaxConcurrentRequests = distributedLimit,
        CircuitBreakerFailureThreshold = threshold,
        CircuitBreakerOpenDuration = openDuration ?? TimeSpan.FromMinutes(1),
        CircuitBreakerHalfOpenMaxProbes = halfOpenProbes
    };
}
