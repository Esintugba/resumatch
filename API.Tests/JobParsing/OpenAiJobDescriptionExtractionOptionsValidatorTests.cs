using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class OpenAiJobDescriptionExtractionOptionsValidatorTests
{
    [Fact]
    public void Validate_DefaultUnavailableProvider_WithDisabledWorker_IsValid()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(
                Options.DefaultName,
                new OpenAiJobDescriptionExtractionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_OpenAiWithoutCredentialOrModel_FailsWithoutSecretDisclosure()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(
                Options.DefaultName,
                new OpenAiJobDescriptionExtractionOptions
                {
                    Provider = JobDescriptionExtractionProviderKind.OpenAI
                });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, item => item.Contains(
            "credential",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Failures, item => item.Contains(
            "model",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WorkerEnabledWithUnavailableProvider_Fails()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions
            {
                WorkerEnabled = true
            })
            .Validate(
                Options.DefaultName,
                new OpenAiJobDescriptionExtractionOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, item => item.Contains(
            "worker",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DisabledWorkerWithUnavailableProvider_Succeeds()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions
            {
                WorkerEnabled = false,
                ProviderExecutionEnabled = true
            })
            .Validate(
                Options.DefaultName,
                new OpenAiJobDescriptionExtractionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ProviderKillSwitchWithUnavailableProvider_Succeeds()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions
            {
                WorkerEnabled = true,
                ProviderExecutionEnabled = false
            })
            .Validate(
                Options.DefaultName,
                new OpenAiJobDescriptionExtractionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RequestTimeoutCannotExceedProviderBudget()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions
            {
                ProviderTimeout = TimeSpan.FromSeconds(20)
            })
            .Validate(
                Options.DefaultName,
                ValidOpenAiOptions(requestTimeout: TimeSpan.FromSeconds(21)));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, item => item.Contains(
            "ProviderTimeout",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Validate_InputAndOutputLimitsMustBePositive(
        int maxInputCharacters,
        int maxOutputTokens)
    {
        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(
                Options.DefaultName,
                ValidOpenAiOptions(
                    maxInputCharacters,
                    maxOutputTokens));

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ValidOpenAiConfiguration_Succeeds()
    {
        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(Options.DefaultName, ValidOpenAiOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RequiredPinnedModelRejectsUnpinnedDeclaration()
    {
        var options = new OpenAiJobDescriptionExtractionOptions
        {
            Provider = JobDescriptionExtractionProviderKind.OpenAI,
            ApiKey = "test-key",
            Model = "operator-declared-pinned-model",
            RequirePinnedModel = true,
            ModelIsPinned = false,
            MaxInputCharacters = 10_000,
            MaxOutputTokens = 2_048,
            MaxConcurrentRequests = 1,
            RequestTimeout = TimeSpan.FromSeconds(10)
        };

        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("pinned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RequiredPinnedModelAcceptsExplicitPinnedDeclaration()
    {
        var options = new OpenAiJobDescriptionExtractionOptions
        {
            Provider = JobDescriptionExtractionProviderKind.OpenAI,
            ApiKey = "test-key",
            Model = "operator-declared-pinned-model",
            RequirePinnedModel = true,
            ModelIsPinned = true,
            MaxInputCharacters = 10_000,
            MaxOutputTokens = 2_048,
            MaxConcurrentRequests = 1,
            RequestTimeout = TimeSpan.FromSeconds(10)
        };

        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationFailures_NeverContainConfiguredApiKey()
    {
        const string secret = "super-sensitive-provider-key";
        var options = ValidOpenAiOptions(
            apiKey: secret,
            requestTimeout: TimeSpan.FromDays(1));

        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.All(result.Failures, item => Assert.DoesNotContain(
            secret,
            item,
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100001, 2048, 1)]
    [InlineData(10000, 16385, 1)]
    [InlineData(10000, 2048, 0)]
    [InlineData(10000, 2048, 17)]
    public void Excessive_cost_or_concurrency_limits_are_rejected(
        int inputCharacters,
        int outputTokens,
        int concurrency)
    {
        var options = ValidOpenAiOptions(
            inputCharacters,
            outputTokens,
            maxConcurrentRequests: concurrency);

        var result = CreateValidator(new JobPostingParseDraftOptions())
            .Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    private static OpenAiJobDescriptionExtractionOptionsValidator CreateValidator(
        JobPostingParseDraftOptions draftOptions) =>
        new(Options.Create(draftOptions));

    private static OpenAiJobDescriptionExtractionOptions ValidOpenAiOptions(
        int maxInputCharacters = 10_000,
        int maxOutputTokens = 2_048,
        string apiKey = "test-key",
        TimeSpan? requestTimeout = null,
        int maxConcurrentRequests = 1) =>
        new()
        {
            Provider = JobDescriptionExtractionProviderKind.OpenAI,
            ApiKey = apiKey,
            Model = "test-model",
            MaxInputCharacters = maxInputCharacters,
            MaxOutputTokens = maxOutputTokens,
            MaxConcurrentRequests = maxConcurrentRequests,
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10)
        };
}
