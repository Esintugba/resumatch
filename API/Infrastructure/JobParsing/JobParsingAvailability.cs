using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.JobParsing;

public sealed class JobParsingAvailability : IJobParsingAvailability
{
    public JobParsingAvailability(
        IOptions<OpenAiJobDescriptionExtractionOptions> extractionOptions,
        IOptions<JobPostingParseDraftOptions> draftOptions)
    {
        ArgumentNullException.ThrowIfNull(extractionOptions);
        ArgumentNullException.ThrowIfNull(draftOptions);

        // Without a provider or a worker a requested draft would stay Pending.
        IsEnabled =
            extractionOptions.Value.Provider != JobDescriptionExtractionProviderKind.Unavailable &&
            draftOptions.Value.WorkerEnabled;
    }

    public bool IsEnabled { get; }
}
