namespace Api.Application.JobParsing.Models;

public sealed record JobDescriptionProviderMetadata
{
    public JobDescriptionProviderMetadata(
        string requestId,
        string providerName,
        string modelName,
        ProviderTokenUsage? usage = null)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(providerName) ||
            string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Provider metadata cannot contain blank values.");
        }

        RequestId = requestId;
        ProviderName = providerName;
        ModelName = modelName;
        Usage = usage;
    }

    public string RequestId { get; }
    public string ProviderName { get; }
    public string ModelName { get; }
    public ProviderTokenUsage? Usage { get; }
}
