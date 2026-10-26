using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Engine.Keywords;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Queries;

public readonly record struct JobPostingSkillFingerprintItem(
    Guid SkillId,
    JobSkillRequirementType RequirementType,
    int ImportanceScore,
    string Name = "",
    string? NormalizedName = null);

public static class JobPostingSkillSnapshotHashCalculator
{
    public static string Compute(
        IEnumerable<JobPostingSkillSnapshot> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        return Compute(skills.Select(skill =>
            new JobPostingSkillFingerprintItem(
                skill.Skill.Id,
                skill.RequirementType,
                skill.ImportanceScore,
                skill.Skill.Name,
                skill.Skill.NormalizedName)));
    }

    public static string Compute(
        IEnumerable<JobPostingSkillFingerprintItem> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        var ordered = skills
            .OrderBy(skill => skill.SkillId)
            .ToArray();

        if (ordered.Any(skill => skill.SkillId == Guid.Empty))
        {
            throw new ArgumentException(
                "Skill identifiers cannot be empty.",
                nameof(skills));
        }

        if (ordered
            .GroupBy(skill => skill.SkillId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Skill identifiers must be unique.",
                nameof(skills));
        }

        if (ordered.Any(skill => !Enum.IsDefined(skill.RequirementType)))
        {
            throw new ArgumentException(
                "Skill requirement types must be valid.",
                nameof(skills));
        }

        if (ordered.Any(skill => skill.ImportanceScore is < 0 or > 100))
        {
            throw new ArgumentException(
                "Skill importance scores must be between 0 and 100.",
                nameof(skills));
        }

        using var stream = new MemoryStream();
        WriteInt32(stream, ordered.Length);

        foreach (var skill in ordered)
        {
            var skillIdBytes = Encoding.UTF8.GetBytes(
                skill.SkillId.ToString("D"));
            WriteInt32(stream, skillIdBytes.Length);
            stream.Write(skillIdBytes);
            WriteInt32(stream, (int)skill.RequirementType);
            WriteInt32(stream, skill.ImportanceScore);
            WriteString(stream, GetCanonicalName(skill));
            WriteString(
                stream,
                string.IsNullOrWhiteSpace(skill.Name)
                    ? GetCanonicalName(skill)
                    : skill.Name.Trim());
        }

        return Convert.ToHexString(
                SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)))
            .ToLowerInvariant();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string GetCanonicalName(
        JobPostingSkillFingerprintItem skill)
    {
        var normalizedName = TextNormalizer.Normalize(skill.NormalizedName);
        if (normalizedName.Length > 0)
        {
            return normalizedName;
        }

        var fallbackName = TextNormalizer.Normalize(skill.Name);
        return fallbackName.Length > 0
            ? fallbackName
            : $"id:{skill.SkillId:D}";
    }
}
