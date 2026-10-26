namespace Api.Infrastructure.JobParsing.OpenAI.Contracts;

public sealed record ProviderFacingEvidence
{
    public ProviderFacingEvidence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}
