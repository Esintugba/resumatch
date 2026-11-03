using System.Buffers.Text;
using System.Security.Cryptography;

namespace Api.Application.Auth;

/// <summary>
/// Refresh tokens are 32 random bytes in base64url. The database stores only
/// their SHA-256 hash; 256 bits of entropy make a slow hash unnecessary.
/// </summary>
public static class RefreshTokenCodec
{
    private const int TokenBytes = 32;

    public static (string Token, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    public static bool TryHash(string? token, out byte[] hash)
    {
        hash = [];
        if (token is not { Length: 43 } || !token.All(IsBase64UrlCharacter))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[48];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var written) ||
            written != TokenBytes)
        {
            return false;
        }

        hash = SHA256.HashData(bytes[..written]);
        return true;
    }

    private static bool IsBase64UrlCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
