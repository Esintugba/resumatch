namespace Api.Application.JobParsing.Models;

public sealed record ProviderExtractionResult
{
    public ProviderExtractionResult(
        string providerRequestId,
        string providerName,
        string modelName,
        string rawJsonPayload,
        ProviderTokenUsage? usage = null)
    {
        if (string.IsNullOrWhiteSpace(providerRequestId))
        {
            throw new ArgumentException(
                "Provider request identifier is required.",
                nameof(providerRequestId));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException(
                "Provider name is required.",
                nameof(providerName));
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException(
                "Provider model name is required.",
                nameof(modelName));
        }

        ArgumentNullException.ThrowIfNull(rawJsonPayload);

        ProviderRequestId = providerRequestId;
        ProviderName = providerName;
        ModelName = modelName;
        RawJsonPayload = rawJsonPayload;
        Usage = usage;
    }

    public string ProviderRequestId { get; }
    public string ProviderName { get; }
    public string ModelName { get; }
    /// <summary>
    /// Gets the adapter-normalized JSON payload that follows the parser's
    /// internal strict contract. It is not necessarily the provider's raw
    /// wire response and must not be persisted as such.
    /// </summary>
    public string RawJsonPayload { get; }
    public ProviderTokenUsage? Usage { get; }
}
