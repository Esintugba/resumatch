using Api.Application.JobParsing;
using Api.Application.JobParsing.Models;

namespace API.Tests.JobParsing;

internal sealed class FakeJobDescriptionExtractionProvider(
    string rawJsonPayload) : IJobDescriptionExtractionProvider
{
    public string RawJsonPayload { get; set; } = rawJsonPayload;
    public Exception? ExceptionToThrow { get; set; }
    public bool SimulateCancellation { get; set; }
    public int CallCount { get; private set; }
    public ProviderExtractionRequest? LastRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public ProviderTokenUsage? Usage { get; set; }

    public async Task<ProviderExtractionResult> ExtractAsync(
        ProviderExtractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;

        if (SimulateCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return new ProviderExtractionResult(
            "fake-request-id",
            "fake-provider",
            "fake-model",
            RawJsonPayload,
            Usage);
    }
}
