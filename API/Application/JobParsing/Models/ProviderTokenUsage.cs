namespace Api.Application.JobParsing.Models;

public sealed record ProviderTokenUsage(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens);
