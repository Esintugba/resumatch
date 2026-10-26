using Api.Application.JobParsing;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing.OpenAI;

public sealed class OpenAiJobDescriptionExtractionOptionsValidator :
    IValidateOptions<OpenAiJobDescriptionExtractionOptions>
{
    private const int MaximumModelNameLength = 200;
    private const int MaximumInputCharacters = 100_000;
    private const int MaximumOutputTokens = 16_384;
    private const int MaximumConcurrentRequests = 16;

    private readonly IOptions<JobPostingParseDraftOptions> _draftOptions;

    public OpenAiJobDescriptionExtractionOptionsValidator(
        IOptions<JobPostingParseDraftOptions> draftOptions)
    {
        _draftOptions = draftOptions ??
            throw new ArgumentNullException(nameof(draftOptions));
    }

    public ValidateOptionsResult Validate(
        string? name,
        OpenAiJobDescriptionExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var draftOptions = _draftOptions.Value;

        if (!Enum.IsDefined(options.Provider))
        {
            failures.Add("Extraction provider selection is invalid.");
        }

        if (options.MaxInputCharacters is <= 0 or > MaximumInputCharacters)
        {
            failures.Add(
                $"MaxInputCharacters must be between 1 and {MaximumInputCharacters}.");
        }

        if (options.MaxOutputTokens is <= 0 or > MaximumOutputTokens)
        {
            failures.Add(
                $"MaxOutputTokens must be between 1 and {MaximumOutputTokens}.");
        }

        if (options.MaxConcurrentRequests is <= 0 or > MaximumConcurrentRequests)
        {
            failures.Add(
                $"MaxConcurrentRequests must be between 1 and {MaximumConcurrentRequests}.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            failures.Add("RequestTimeout must be greater than zero.");
        }
        else if (options.RequestTimeout > TimeSpan.FromMinutes(2))
        {
            failures.Add("RequestTimeout cannot exceed two minutes.");
        }
        else if (options.RequestTimeout > draftOptions.ProviderTimeout)
        {
            failures.Add(
                "RequestTimeout cannot exceed the draft ProviderTimeout budget.");
        }

        if (draftOptions.WorkerEnabled &&
            draftOptions.ProviderExecutionEnabled &&
            options.Provider == JobDescriptionExtractionProviderKind.Unavailable)
        {
            failures.Add(
                "The parse worker cannot be enabled while the extraction provider is unavailable.");
        }

        if (options.Provider == JobDescriptionExtractionProviderKind.OpenAI)
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add("An API credential is required for the OpenAI provider.");
            }

            if (string.IsNullOrWhiteSpace(options.Model) ||
                options.Model.Length > MaximumModelNameLength)
            {
                failures.Add("A valid OpenAI model must be configured.");
            }

            if (options.RequirePinnedModel && !options.ModelIsPinned)
            {
                failures.Add(
                    "The configured OpenAI model must be explicitly marked as pinned when RequirePinnedModel is enabled.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
