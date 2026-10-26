namespace Api.Application.JobParsing.Models;

public sealed record ProviderExtractionRequest
{
    public ProviderExtractionRequest(
        string title,
        string description,
        string parserVersion)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Job posting title is required.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Job posting description is required.",
                nameof(description));
        }

        if (string.IsNullOrWhiteSpace(parserVersion))
        {
            throw new ArgumentException(
                "Parser version is required.",
                nameof(parserVersion));
        }

        Title = title;
        Description = description;
        ParserVersion = parserVersion;
    }

    public string Title { get; }
    public string Description { get; }
    public string ParserVersion { get; }
}
