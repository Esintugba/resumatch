using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Api.Application.JobParsing.Engine.Preprocessing;

public static class SourceInputHashCalculator
{
    public static string Compute(string title, string description)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, title);
        AppendField(hash, description);

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);

        hash.AppendData(lengthPrefix);
        hash.AppendData(bytes);
    }
}
