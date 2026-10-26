using System.Globalization;
using System.Text;

namespace Api.Application.JobParsing.Engine.Preprocessing;

public sealed class JobDescriptionPreprocessor
{
    public const int MaximumDescriptionLength = 50_000;

    public JobDescriptionPreprocessingResult Process(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Job description is required.",
                nameof(description));
        }

        if (description.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"Job description cannot exceed {MaximumDescriptionLength} characters.",
                nameof(description));
        }

        ValidateControlCharacters(description);

        return new JobDescriptionPreprocessingResult(
            description,
            NormalizeForMatching(description),
            DescriptionHashCalculator.Compute(description));
    }

    public static string NormalizeForMatching(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        var previousWasWhitespace = true;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    result.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            result.Append(character);
            previousWasWhitespace = false;
        }

        if (result.Length > 0 && result[^1] == ' ')
        {
            result.Length--;
        }

        return result.ToString();
    }

    private static void ValidateControlCharacters(string description)
    {
        foreach (var character in description)
        {
            if (char.GetUnicodeCategory(character) == UnicodeCategory.Control &&
                character is not '\r' and not '\n' and not '\t')
            {
                throw new ArgumentException(
                    "Job description contains an unsupported control character.",
                    nameof(description));
            }
        }
    }
}
