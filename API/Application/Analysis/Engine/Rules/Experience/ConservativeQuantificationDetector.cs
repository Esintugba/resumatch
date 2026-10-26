using System.Globalization;
using System.Text.RegularExpressions;

namespace Api.Application.Analysis.Engine.Rules.Experience;

internal static partial class ConservativeQuantificationDetector
{
    public static bool ContainsQuantifiedOutcome(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = RuleText.NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (PercentagePattern().IsMatch(normalized) ||
            MoneyPattern().IsMatch(normalized))
        {
            return true;
        }

        if (MultiplierPattern().IsMatch(normalized) &&
            ImpactTermPattern().IsMatch(normalized))
        {
            return true;
        }

        foreach (Match match in ExplicitCountPattern().Matches(normalized))
        {
            var numberGroup = match.Groups["number"];
            if (!IsExcludedNumericContext(
                    normalized,
                    numberGroup.Index,
                    numberGroup.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcludedNumericContext(
        string content,
        int numberIndex,
        string numberText)
    {
        var digits = numberText.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        if (int.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number) &&
            number is >= 1900 and <= 2099)
        {
            return true;
        }

        var prefixStart = Math.Max(0, numberIndex - 12);
        var prefix = content[prefixStart..numberIndex];
        return TechnologyPrefixPattern().IsMatch(prefix);
    }

    [GeneratedRegex(
        @"(?:%\s*\d+(?:[.,]\d+)?|\b(?:percent|yüzde)\s+\d+(?:[.,]\d+)?\b|\b\d+(?:[.,]\d+)?\s*(?:%|percent\b|yüzde\b))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentagePattern();

    [GeneratedRegex(
        @"(?:[$€£₺]\s*\d[\d.,]*|\b\d[\d.,]*\s*(?:USD|EUR|TRY|GBP|TL)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(
        @"\b\d+(?:[.,]\d+)?\s*[xX]\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MultiplierPattern();

    [GeneratedRegex(
        @"\b(?:increase|increased|improve|improved|improvement|growth|faster|speed|reduce|reduced|reduction|decrease|decreased|artış|arttır|iyileş|büyü|hız|azal|düşür)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImpactTermPattern();

    [GeneratedRegex(
        @"\b(?<number>\d+(?:[.,]\d+)?)\s+(?:users?|customers?|clients?|requests?|transactions?|orders?|records?|applications?|services?|endpoints?|features?|projects?|teams?|kullanıcı|kullanıcılar|müşteri|müşteriler|istek|istekler|işlem|işlemler|sipariş|siparişler|kayıt|kayıtlar|uygulama|uygulamalar|servis|servisler|proje|projeler)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitCountPattern();

    [GeneratedRegex(
        @"(?:\.NET|C#|HTTP|RFC|ISO)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TechnologyPrefixPattern();
}
