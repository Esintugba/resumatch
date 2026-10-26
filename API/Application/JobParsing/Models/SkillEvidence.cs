namespace Api.Application.JobParsing.Models;

public sealed record SkillEvidence
{
    /// <summary>
    /// Creates evidence whose offset and length use .NET string semantics:
    /// both values count UTF-16 code units in the canonical source text.
    /// A provider using another offset convention must be converted and
    /// verified before constructing this model.
    /// </summary>
    public SkillEvidence(string text, int start, int length)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Evidence text is required.",
                nameof(text));
        }

        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                "Evidence start cannot be negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Evidence length must be greater than zero.");
        }

        if (text.Length != length)
        {
            throw new ArgumentException(
                "Evidence length must equal the UTF-16 length of its text.",
                nameof(length));
        }

        if ((long)start + length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Evidence range exceeds the supported string index range.");
        }

        Text = text;
        Start = start;
        Length = length;
    }

    public string Text { get; }
    public int Start { get; }
    public int Length { get; }
}
