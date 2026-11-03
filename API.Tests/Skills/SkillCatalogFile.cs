using Api.Application.Skills;
using Api.Entities;
using Api.Entities.Enums;

namespace API.Tests.Skills;

public sealed record SkillCatalogRow(
    string Name,
    SkillCategory Category,
    string? NameTr,
    IReadOnlyList<string> SearchAliases)
{
    public string NormalizedName => Name.ToLowerInvariant();

    public string? NormalizedNameTr =>
        NameTr is null ? null : SkillSearchText.Normalize(NameTr);

    public Skill ToSkill() => new()
    {
        Id = Guid.NewGuid(),
        Name = Name,
        NormalizedName = NormalizedName,
        Category = Category,
        NameTr = NameTr,
        NormalizedNameTr = NormalizedNameTr,
        SearchAliases = [.. SearchAliases]
    };
}

public static class SkillCatalogFile
{
    public const int CurrentVersion = 2;

    public static IReadOnlyList<SkillCatalogRow> Read(int version = CurrentVersion)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ResuMatch.slnx")))
        {
            directory = directory.Parent;
        }

        return File
            .ReadAllLines(Path.Combine(
                directory.FullName,
                "API",
                "Data",
                "SkillCatalog",
                $"skill-catalog-v{version}.csv"))
            .Skip(1)
            .Select(line => line.Split(','))
            .Select(fields => new SkillCatalogRow(
                fields[0],
                Enum.Parse<SkillCategory>(fields[1]),
                fields[2].Length == 0 ? null : fields[2],
                fields.Length > 3 && fields[3].Length > 0 ? fields[3].Split('|') : []))
            .ToArray();
    }
}
