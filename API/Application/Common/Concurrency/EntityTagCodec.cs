using System.Globalization;

namespace Api.Application.Common.Concurrency;

public static class EntityTagCodec
{
    public static string Format(uint version)
    {
        return $"\"{version:X8}\"";
    }

    public static bool TryParse(string? entityTag, out uint version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(entityTag))
        {
            return false;
        }

        var value = entityTag.Trim();
        if (value.Length != 10 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        return uint.TryParse(
            value.AsSpan(1, 8),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out version);
    }
}
