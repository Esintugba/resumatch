using Api.Application.Analysis.Engine.Keywords;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Application.Skills;
using Api.Entities.Enums;

namespace API.Tests.Skills;

public sealed class SkillCatalogFileTests
{
    private static readonly IReadOnlyList<SkillCatalogRow> Rows = SkillCatalogFile.Read();

    [Fact]
    public void Catalog_HasThePlannedSizeAndDistribution()
    {
        Assert.Equal(399, Rows.Count);
        var expected = new Dictionary<SkillCategory, int>
        {
            [SkillCategory.ProgrammingLanguage] = 29,
            [SkillCategory.Frontend] = 40,
            [SkillCategory.Backend] = 50,
            [SkillCategory.Database] = 30,
            [SkillCategory.DevOps] = 45,
            [SkillCategory.Cloud] = 35,
            [SkillCategory.Testing] = 25,
            [SkillCategory.Architecture] = 25,
            [SkillCategory.Tool] = 50,
            [SkillCategory.SoftSkill] = 40,
            [SkillCategory.Other] = 30
        };

        Assert.Equal(
            expected.OrderBy(item => item.Key),
            Rows.GroupBy(row => row.Category)
                .ToDictionary(group => group.Key, group => group.Count())
                .OrderBy(item => item.Key));
    }

    [Fact]
    public void NormalizedNames_AreUniqueAcrossCategories()
    {
        Assert.Equal(Rows.Count, Rows.Select(row => row.NormalizedName).Distinct().Count());
    }

    [Fact]
    public void NormalizedTurkishNames_AreUniqueAndDoNotCollideWithNames()
    {
        var turkish = Rows
            .Where(row => row.NormalizedNameTr is not null)
            .Select(row => row.NormalizedNameTr!)
            .ToArray();

        Assert.Equal(turkish.Length, turkish.Distinct().Count());
        Assert.Empty(turkish.Intersect(Rows.Select(row => row.NormalizedName)));
    }

    [Theory]
    [InlineData("react.js")]
    [InlineData("reactjs")]
    [InlineData("node")]
    [InlineData("nodejs")]
    [InlineData("postgres")]
    [InlineData("postgre")]
    [InlineData("golang")]
    [InlineData("vue")]
    [InlineData("vuejs")]
    [InlineData("nextjs")]
    [InlineData("js")]
    [InlineData("ts")]
    [InlineData("k8s")]
    [InlineData("mongo")]
    [InlineData("dotnet")]
    [InlineData(".net core")]
    [InlineData("csharp")]
    [InlineData("c sharp")]
    [InlineData("cplusplus")]
    [InlineData("express.js")]
    [InlineData("nuxt.js")]
    [InlineData("amazon web services")]
    [InlineData("azure")]
    [InlineData("gcp")]
    [InlineData("scss")]
    [InlineData("html5")]
    [InlineData("css3")]
    [InlineData("rest")]
    [InlineData("swagger")]
    [InlineData("kafka")]
    [InlineData("excel")]
    [InlineData("shell scripting")]
    [InlineData("shell")]
    public void CommonVariants_AreNotSeparateRecords(string variant)
    {
        Assert.DoesNotContain(Rows, row => row.NormalizedName == variant);
    }

    [Fact]
    public void ParserAliases_PointToCatalogNames()
    {
        Assert.All(SkillAliasCatalogV1.Aliases.Values, canonicalName =>
            Assert.Contains(Rows, row => row.NormalizedName == canonicalName));
    }

    [Fact]
    public void NormalizedNames_MatchWhatTheEngineComputes()
    {
        Assert.All(Rows, row =>
        {
            Assert.Equal(row.NormalizedName, JobDescriptionPreprocessor.NormalizeForMatching(row.Name));
            Assert.Equal(row.NormalizedName, TextNormalizer.Normalize(row.Name));
        });
    }

    [Theory]
    [InlineData("C#", "c#")]
    [InlineData("C++", "c++")]
    [InlineData("F#", "f#")]
    [InlineData(".NET", ".net")]
    [InlineData("ASP.NET Core", "asp.net core")]
    [InlineData("Objective-C", "objective-c")]
    [InlineData("CI/CD", "ci/cd")]
    [InlineData("OAuth 2.0", "oauth 2.0")]
    public void SpecialCharacters_SurviveNormalization(string name, string expected)
    {
        var row = Assert.Single(Rows, item => item.Name == name);

        Assert.Equal(expected, row.NormalizedName);
        Assert.Equal(expected, SkillSearchText.Normalize(name));
    }

    [Theory]
    [InlineData("İLETİŞİM", "iletisim")]
    [InlineData("iletişim", "iletisim")]
    [InlineData("ILETISIM", "iletisim")]
    [InlineData("  Iletişim ", "iletisim")]
    [InlineData("YAZILIM", "yazilim")]
    [InlineData("yazılım", "yazilim")]
    [InlineData("ÇATIŞMA", "catisma")]
    [InlineData("ağ", "ag")]
    [InlineData("ĞÜŞİÖÇ", "gusioc")]
    [InlineData("ğüşıöç", "gusioc")]
    [InlineData("zekâ", "zeka")]
    public void SearchText_FoldsTurkishLettersAndCase(string value, string expected)
    {
        Assert.Equal(expected, SkillSearchText.Normalize(value));
    }

    [Fact]
    public void CatalogHash_ChangesWithANewRow()
    {
        var first = new SkillCatalogItem(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "C#",
            "c#",
            SkillCategory.ProgrammingLanguage);
        var second = new SkillCatalogItem(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "Go",
            "go",
            SkillCategory.ProgrammingLanguage);

        Assert.NotEqual(
            SkillCatalogHashCalculator.Compute([first]),
            SkillCatalogHashCalculator.Compute([first, second]));
    }
}
