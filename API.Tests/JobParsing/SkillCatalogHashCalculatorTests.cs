using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class SkillCatalogHashCalculatorTests
{
    [Fact]
    public void Compute_SameCatalogInDifferentOrder_ReturnsSameHash()
    {
        var catalog = Catalog();

        var first = SkillCatalogHashCalculator.Compute(catalog);
        var second = SkillCatalogHashCalculator.Compute(catalog.Reverse());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_ChangedCatalogField_ChangesHash()
    {
        var catalog = Catalog();
        var changed = catalog.ToArray();
        changed[0] = new SkillCatalogItem(
            changed[0].Id,
            "C Sharp",
            changed[0].NormalizedName,
            changed[0].Category);

        Assert.NotEqual(
            SkillCatalogHashCalculator.Compute(catalog),
            SkillCatalogHashCalculator.Compute(changed));
    }

    [Fact]
    public void Compute_ResultIsLowercaseSha256Hex()
    {
        var hash = SkillCatalogHashCalculator.Compute(Catalog());

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Compute_FieldBoundariesCannotCollide()
    {
        var first = new[]
        {
            new SkillCatalogItem(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "ab",
                "c",
                SkillCategory.Other)
        };
        var second = new[]
        {
            new SkillCatalogItem(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "a",
                "bc",
                SkillCategory.Other)
        };

        Assert.NotEqual(
            SkillCatalogHashCalculator.Compute(first),
            SkillCatalogHashCalculator.Compute(second));
    }

    private static SkillCatalogItem[] Catalog() =>
    [
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "C#",
            "c#",
            SkillCategory.ProgrammingLanguage),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "Docker",
            "docker",
            SkillCategory.DevOps)
    ];
}
