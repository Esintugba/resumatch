using Api.Entities.Enums;

namespace Api.Application.JobParsing.Models;

public sealed record SkillCatalogItem
{
    public SkillCatalogItem(
        Guid id,
        string name,
        string normalizedName,
        SkillCategory category)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Skill identifier cannot be empty.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException(
                "Normalized skill name is required.",
                nameof(normalizedName));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                "Skill category is not defined.");
        }

        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        Category = category;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string NormalizedName { get; }
    public SkillCategory Category { get; }
}
