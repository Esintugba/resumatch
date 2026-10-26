namespace Api.Infrastructure.JobParsing.OpenAI;

public sealed class OpenAiJobDescriptionExtractionOptions
{
    public const string SectionName = "JobDescriptionExtraction";

    public JobDescriptionExtractionProviderKind Provider { get; init; } =
        JobDescriptionExtractionProviderKind.Unavailable;

    public string Model { get; init; } = string.Empty;
    public bool RequirePinnedModel { get; init; }
    public bool ModelIsPinned { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public int MaxInputCharacters { get; init; } = 50_250;
    public int MaxOutputTokens { get; init; } = 4_096;
    public int MaxConcurrentRequests { get; init; } = 1;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(25);
}
