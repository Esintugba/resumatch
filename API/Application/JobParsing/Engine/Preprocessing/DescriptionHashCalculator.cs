using System.Security.Cryptography;
using System.Text;

namespace Api.Application.JobParsing.Engine.Preprocessing;

public static class DescriptionHashCalculator
{
    public static string Compute(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        var bytes = Encoding.UTF8.GetBytes(description);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
