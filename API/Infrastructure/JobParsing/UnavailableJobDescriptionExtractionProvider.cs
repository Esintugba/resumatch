using Api.Application.JobParsing;
using Api.Application.JobParsing.Models;

namespace Api.Infrastructure.JobParsing;

public sealed class UnavailableJobDescriptionExtractionProvider :
    IJobDescriptionExtractionProvider
{
    public Task<ProviderExtractionResult> ExtractAsync(
        ProviderExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new HttpRequestException(
            "A production job description extraction provider is not configured.");
    }
}
