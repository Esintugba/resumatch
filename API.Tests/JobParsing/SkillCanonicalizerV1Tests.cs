using System.Collections.Immutable;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Models;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class SkillCanonicalizerV1Tests
{
    private readonly SkillCanonicalizerV1 _canonicalizer = new();

    [Fact]
    public void Resolve_CSharpAndCpp_RemainSeparate()
    {
        var catalog = new[]
        {
            Skill(1, "C#", "c#", SkillCategory.ProgrammingLanguage),
            Skill(2, "C++", "c++", SkillCategory.ProgrammingLanguage)
        };

        var csharp = _canonicalizer.Resolve(Candidate("C#"), catalog);
        var cpp = _canonicalizer.Resolve(Candidate("C++"), catalog);

        Assert.Equal(Id(1), csharp.ResolvedSkill!.Id);
        Assert.Equal(Id(2), cpp.ResolvedSkill!.Id);
        Assert.NotEqual(csharp.CanonicalKey, cpp.CanonicalKey);
    }

    [Fact]
    public void Resolve_DotNetAndAspNetCore_RemainSeparate()
    {
        var catalog = new[]
        {
            Skill(1, ".NET", ".net", SkillCategory.Backend),
            Skill(2, "ASP.NET Core", "asp.net core", SkillCategory.Backend)
        };

        var dotnet = _canonicalizer.Resolve(Candidate(".NET"), catalog);
        var aspnet = _canonicalizer.Resolve(Candidate("ASP.NET Core"), catalog);

        Assert.Equal(Id(1), dotnet.ResolvedSkill!.Id);
        Assert.Equal(Id(2), aspnet.ResolvedSkill!.Id);
    }

    [Fact]
    public void Resolve_GoAndGoogle_RemainSeparate()
    {
        var catalog = new[]
        {
            Skill(1, "Go", "go", SkillCategory.ProgrammingLanguage),
            Skill(2, "Google", "google", SkillCategory.Tool)
        };

        var go = _canonicalizer.Resolve(Candidate("Go"), catalog);
        var google = _canonicalizer.Resolve(Candidate("Google"), catalog);

        Assert.Equal(Id(1), go.ResolvedSkill!.Id);
        Assert.Equal(Id(2), google.ResolvedSkill!.Id);
    }

    [Fact]
    public void Resolve_AppliesUnicodeNfkcNormalization()
    {
        var catalog = new[]
        {
            Skill(1, "C#", "c#", SkillCategory.ProgrammingLanguage)
        };

        var result = _canonicalizer.Resolve(Candidate("Ｃ＃"), catalog);

        Assert.Equal(SkillResolutionStatus.Exact, result.Status);
        Assert.Equal("c#", result.CanonicalKey);
    }

    [Fact]
    public void Resolve_NormalizesWhitespace()
    {
        var catalog = new[]
        {
            Skill(1, "ASP.NET Core", "asp.net core", SkillCategory.Backend)
        };

        var result = _canonicalizer.Resolve(
            Candidate("  ASP.NET\t  Core "),
            catalog);

        Assert.Equal(SkillResolutionStatus.Exact, result.Status);
        Assert.Equal("asp.net core", result.CanonicalKey);
    }

    [Fact]
    public void Resolve_ExactNormalizedName_ReturnsExact()
    {
        var skill = Skill(1, "PostgreSQL", "postgresql", SkillCategory.Database);

        var result = _canonicalizer.Resolve(Candidate("POSTGRESQL"), [skill]);

        Assert.Equal(SkillResolutionStatus.Exact, result.Status);
        Assert.Equal(skill, result.ResolvedSkill);
    }

    [Theory]
    [InlineData("postgres", "PostgreSQL", "postgresql")]
    [InlineData("js", "JavaScript", "javascript")]
    [InlineData("ts", "TypeScript", "typescript")]
    public void Resolve_ReviewedAlias_ReturnsAlias(
        string alias,
        string skillName,
        string normalizedName)
    {
        var skill = Skill(1, skillName, normalizedName, SkillCategory.Tool);

        var result = _canonicalizer.Resolve(Candidate(alias), [skill]);

        Assert.Equal(SkillResolutionStatus.Alias, result.Status);
        Assert.Equal(normalizedName, result.CanonicalKey);
        Assert.Equal(skill, result.ResolvedSkill);
    }

    [Fact]
    public void Resolve_AliasWithoutCatalogTarget_ReturnsUnresolvedCandidateKey()
    {
        var result = _canonicalizer.Resolve(Candidate("postgres"), []);

        Assert.Equal(SkillResolutionStatus.Unresolved, result.Status);
        Assert.Equal("postgres", result.CanonicalKey);
        Assert.Null(result.ResolvedSkill);
    }

    [Fact]
    public void Resolve_SameNormalizedNameAcrossCategories_ReturnsAmbiguous()
    {
        var catalog = new[]
        {
            Skill(1, "Azure", "azure", SkillCategory.Cloud),
            Skill(2, "Azure", "azure", SkillCategory.Tool)
        };

        var result = _canonicalizer.Resolve(Candidate("Azure"), catalog);

        Assert.Equal(SkillResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Matches.Length);
        Assert.Null(result.ResolvedSkill);
    }

    [Fact]
    public void Resolve_AliasWithMultipleCatalogTargets_ReturnsAmbiguous()
    {
        var catalog = new[]
        {
            Skill(1, "JavaScript", "javascript", SkillCategory.Frontend),
            Skill(2, "JavaScript", "javascript", SkillCategory.ProgrammingLanguage)
        };

        var result = _canonicalizer.Resolve(Candidate("js"), catalog);

        Assert.Equal(SkillResolutionStatus.Ambiguous, result.Status);
        Assert.Equal("javascript", result.CanonicalKey);
        Assert.Null(result.ResolvedSkill);
    }

    [Fact]
    public void Resolve_FuzzyName_DoesNotMatch()
    {
        var catalog = new[]
        {
            Skill(1, "JavaScript", "javascript", SkillCategory.ProgrammingLanguage)
        };

        var result = _canonicalizer.Resolve(Candidate("javascrip"), catalog);

        Assert.Equal(SkillResolutionStatus.Unresolved, result.Status);
    }

    [Fact]
    public void Resolve_Substring_DoesNotMatch()
    {
        var catalog = new[]
        {
            Skill(1, "Google", "google", SkillCategory.Tool)
        };

        var result = _canonicalizer.Resolve(Candidate("Go"), catalog);

        Assert.Equal(SkillResolutionStatus.Unresolved, result.Status);
    }

    [Fact]
    public void Resolve_EquivalentCandidateForms_ProduceSameCanonicalKey()
    {
        var catalog = new[]
        {
            Skill(1, "ASP.NET Core", "asp.net core", SkillCategory.Backend)
        };

        var first = _canonicalizer.Resolve(Candidate("ASP.NET Core"), catalog);
        var second = _canonicalizer.Resolve(
            Candidate("  ＡＳＰ．ＮＥＴ\tCore  "),
            catalog);

        Assert.Equal(first.CanonicalKey, second.CanonicalKey);
        Assert.Equal(first.ResolvedSkill, second.ResolvedSkill);
    }

    [Fact]
    public void Resolve_UnresolvedCandidate_DoesNotCreateCatalogSkill()
    {
        var catalog = new List<SkillCatalogItem>();

        var result = _canonicalizer.Resolve(Candidate("Unknown Skill"), catalog);

        Assert.Equal(SkillResolutionStatus.Unresolved, result.Status);
        Assert.Empty(result.Matches);
        Assert.Empty(catalog);
    }

    private static ExtractedSkillCandidate Candidate(string name)
    {
        return new ExtractedSkillCandidate(
            name,
            name,
            JobSkillRequirementType.Mentioned,
            null,
            [new SkillEvidence(name, 0, name.Length)]);
    }

    private static SkillCatalogItem Skill(
        int id,
        string name,
        string normalizedName,
        SkillCategory category)
    {
        return new SkillCatalogItem(Id(id), name, normalizedName, category);
    }

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
