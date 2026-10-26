using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing.Engine.Preprocessing;

public static class SkillCatalogHashCalculator
{
    public static string Compute(IEnumerable<SkillCatalogItem> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var ordered = skills
            .OrderBy(skill => skill.NormalizedName, StringComparer.Ordinal)
            .ThenBy(skill => skill.Category)
            .ThenBy(skill => skill.Id)
            .ToImmutableArray();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, ordered.Length);
        foreach (var skill in ordered)
        {
            AppendString(hash, skill.Id.ToString("D"));
            AppendString(hash, skill.Name);
            AppendString(hash, skill.NormalizedName);
            AppendInt32(hash, (int)skill.Category);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
