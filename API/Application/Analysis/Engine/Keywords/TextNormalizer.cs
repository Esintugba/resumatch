using System.Text;

namespace Api.Application.Analysis.Engine.Keywords;

public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Trim()
            .ToLowerInvariant();

        return string.Join(
            ' ',
            normalized.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }

    public static bool ContainsExactPhrase(string? text, string? phrase)
    {
        var normalizedText = Normalize(text);
        var normalizedPhrase = Normalize(phrase);

        if (normalizedText.Length == 0 || normalizedPhrase.Length == 0)
        {
            return false;
        }

        var searchIndex = 0;
        while (searchIndex <= normalizedText.Length - normalizedPhrase.Length)
        {
            var matchIndex = normalizedText.IndexOf(
                normalizedPhrase,
                searchIndex,
                StringComparison.Ordinal);

            if (matchIndex < 0)
            {
                return false;
            }

            var matchEnd = matchIndex + normalizedPhrase.Length;
            var hasLeftBoundary =
                matchIndex == 0 ||
                !IsTechnologyIdentifierCharacter(normalizedText[matchIndex - 1]);
            var hasRightBoundary = HasRightBoundary(normalizedText, matchEnd);

            if (hasLeftBoundary && hasRightBoundary)
            {
                return true;
            }

            searchIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool IsTechnologyIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
               value is '+' or '#' or '.' or '-' or '_';
    }

    private static bool HasRightBoundary(string text, int matchEnd)
    {
        if (matchEnd == text.Length)
        {
            return true;
        }

        var nextCharacter = text[matchEnd];
        if (char.IsLetterOrDigit(nextCharacter) ||
            nextCharacter is '+' or '#' or '_')
        {
            return false;
        }

        if (nextCharacter is '.' or '-')
        {
            return matchEnd + 1 == text.Length ||
                   !IsTechnologyIdentifierCharacter(text[matchEnd + 1]);
        }

        return true;
    }
}
