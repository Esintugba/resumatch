using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing;

public interface IJobDescriptionExtractionProvider
{
    Task<ProviderExtractionResult> ExtractAsync(
        ProviderExtractionRequest request,
        CancellationToken cancellationToken);
}
