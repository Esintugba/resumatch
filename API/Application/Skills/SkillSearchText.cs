using System.Globalization;
using System.Text;

namespace Api.Application.Skills;

/// <summary>
/// Folds text for skill search so that Turkish letters, their ASCII spellings
/// and either case find the same skill: "İLETİŞİM", "iletişim" and "iletisim"
/// are equal. Used only by search, never by the matching engine.
/// </summary>
public static class SkillSearchText
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark)
            {
                result.Append(character == 'ı' ? 'i' : char.ToLowerInvariant(character));
            }
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
