namespace Api.Infrastructure.Authentication;

internal static class JwtSigningKeyMaterial
{
    public const int MinimumSizeInBytes = 32;

    public static bool IsValid(string? encodedKey)
    {
        return TryDecode(encodedKey, out _);
    }

    public static byte[] Decode(string? encodedKey)
    {
        if (TryDecode(encodedKey, out var keyBytes))
        {
            return keyBytes;
        }

        throw new InvalidOperationException(
            $"JWT secret key must be valid Base64 that decodes to at least " +
            $"{MinimumSizeInBytes} bytes. Generate it with a cryptographically secure random source.");
    }

    private static bool TryDecode(string? encodedKey, out byte[] keyBytes)
    {
        keyBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return false;
        }

        try
        {
            keyBytes = Convert.FromBase64String(encodedKey);
            return keyBytes.Length >= MinimumSizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
