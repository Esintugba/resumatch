using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Microsoft.Extensions.Options;

namespace API.Tests.JobParsing;

public sealed class JobParsingAvailabilityTests
{
    [Theory]
    [InlineData(JobDescriptionExtractionProviderKind.Unavailable, false, true, false)]
    [InlineData(JobDescriptionExtractionProviderKind.Unavailable, true, true, false)]
    [InlineData(JobDescriptionExtractionProviderKind.OpenAI, false, true, false)]
    [InlineData(JobDescriptionExtractionProviderKind.OpenAI, true, true, true)]
    [InlineData(JobDescriptionExtractionProviderKind.OpenAI, true, false, true)]
    public void IsEnabled_RequiresAProviderAndAWorker(
        JobDescriptionExtractionProviderKind provider,
        bool workerEnabled,
        bool providerExecutionEnabled,
        bool expected)
    {
        var availability = new JobParsingAvailability(
            Options.Create(new OpenAiJobDescriptionExtractionOptions { Provider = provider }),
            Options.Create(new JobPostingParseDraftOptions
            {
                WorkerEnabled = workerEnabled,
                ProviderExecutionEnabled = providerExecutionEnabled
            }));

        Assert.Equal(expected, availability.IsEnabled);
    }
}
