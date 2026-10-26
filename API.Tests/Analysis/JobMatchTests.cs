using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.JobMatching;
using Api.Application.Analysis.Engine.Keywords;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

public sealed class JobMatchTests
{
    private readonly JobMatchCalculator _calculator =
        new(new ExactKeywordMatcher());

    [Fact]
    public void Match_UsesExplicitResumeSkillAsStrongestEvidence()
    {
        var skill = EngineTestData.Skill(100, "C#", "c#");
        var result = Calculate(
            EngineTestData.Resume(
                skills: [EngineTestData.ResumeSkill(skill)]),
            EngineTestData.Job(Required(1, skill)));

        var match = Assert.Single(result.KeywordMatches);
        Assert.Equal(KeywordMatchStatus.Matched, match.Status);
        Assert.Equal("Skills", match.ResumeSection);
    }

    [Fact]
    public void Match_UsesProjectSkillEvidence()
    {
        var skill = EngineTestData.Skill(101, "Docker", "docker");
        var result = Calculate(
            EngineTestData.Resume(
                projects: [EngineTestData.Project(10, skills: [skill])]),
            EngineTestData.Job(Required(1, skill)));

        var match = Assert.Single(result.KeywordMatches);
        Assert.Equal(KeywordMatchStatus.Matched, match.Status);
        Assert.Equal("Projects", match.ResumeSection);
    }

    [Fact]
    public void Match_UsesExactExperienceTextEvidence()
    {
        var skill = EngineTestData.Skill(
            102,
            "ASP.NET Core",
            "asp.net core");
        var result = Calculate(
            EngineTestData.Resume(experiences:
            [
                EngineTestData.Experience(
                    20,
                    position: "ASP.NET Core Developer",
                    description: null)
            ]),
            EngineTestData.Job(Required(1, skill)));

        var match = Assert.Single(result.KeywordMatches);
        Assert.Equal(KeywordMatchStatus.Matched, match.Status);
        Assert.Equal("Experience", match.ResumeSection);
    }

    [Fact]
    public void Match_SameSkillAcrossEvidenceSources_ProducesOneResult()
    {
        var skill = EngineTestData.Skill(103, "Docker", "docker");
        var resume = EngineTestData.Resume(
            skills: [EngineTestData.ResumeSkill(skill)],
            projects:
            [
                EngineTestData.Project(
                    30,
                    description: "Docker based platform",
                    skills: [skill])
            ],
            experiences:
            [
                EngineTestData.Experience(
                    31,
                    description: "Deployed services with Docker",
                    bullets:
                    [
                        EngineTestData.Bullet(
                            32,
                            "Maintained Docker build pipelines")
                    ])
            ]);

        var result = Calculate(
            resume,
            EngineTestData.Job(Required(1, skill)));

        var match = Assert.Single(result.KeywordMatches);
        Assert.Equal("Skills", match.ResumeSection);
    }

    [Fact]
    public void Match_CanonicalDuplicateJobSkills_AreScoredOnce()
    {
        var first = EngineTestData.Skill(104, ".NET", ".net");
        var duplicate = EngineTestData.Skill(105, "dotnet", ".net");
        var job = EngineTestData.Job(
            EngineTestData.JobSkill(
                1,
                first,
                JobSkillRequirementType.Preferred,
                90),
            EngineTestData.JobSkill(
                2,
                duplicate,
                JobSkillRequirementType.Required,
                80));

        var result = Calculate(EngineTestData.Resume(), job);

        var match = Assert.Single(result.KeywordMatches);
        Assert.Equal(duplicate.Id, match.SkillId);
        Assert.Equal(100m, match.Weight);
    }

    [Fact]
    public void Weighting_WithAllGroups_AssignsRequiredSeventyPercent()
    {
        var result = CalculateThreeRequirementGroups();

        Assert.Equal(70m, WeightFor(result, "RequiredSkill"));
    }

    [Fact]
    public void Weighting_WithAllGroups_AssignsPreferredTwentyPercent()
    {
        var result = CalculateThreeRequirementGroups();

        Assert.Equal(20m, WeightFor(result, "PreferredSkill"));
    }

    [Fact]
    public void Weighting_WithAllGroups_AssignsMentionedTenPercent()
    {
        var result = CalculateThreeRequirementGroups();

        Assert.Equal(10m, WeightFor(result, "MentionedSkill"));
    }

    [Fact]
    public void Weighting_WhenGroupMissing_RenormalizesAcrossActiveGroups()
    {
        var required = EngineTestData.Skill(110, "RequiredSkill");
        var preferred = EngineTestData.Skill(111, "PreferredSkill");
        var result = Calculate(
            EngineTestData.Resume(),
            EngineTestData.Job(
                Required(1, required),
                EngineTestData.JobSkill(
                    2,
                    preferred,
                    JobSkillRequirementType.Preferred)));

        Assert.Equal(77.78m, WeightFor(result, "RequiredSkill"));
        Assert.Equal(22.22m, WeightFor(result, "PreferredSkill"));
    }

    [Fact]
    public void MissingHighImportanceRequiredSkill_ProducesCriticalIssue()
    {
        var skill = EngineTestData.Skill(112, "Kubernetes", "kubernetes");
        var result = Calculate(
            EngineTestData.Resume(),
            EngineTestData.Job(EngineTestData.JobSkill(
                1,
                skill,
                JobSkillRequirementType.Required,
                70)));

        var issue = Assert.Single(
            result.Issues.Where(issue => issue.Code == "JOB.SKL.001"));
        Assert.Equal(IssueSeverity.Critical, issue.Severity);
    }

    [Fact]
    public void Calculate_WithoutRequirements_ReturnsNullScoreAndInputIssue()
    {
        var result = Calculate(
            EngineTestData.Resume(),
            EngineTestData.Job());

        Assert.Null(result.JobMatchScore);
        Assert.Empty(result.KeywordMatches);
        Assert.Contains(result.Issues, issue => issue.Code == "JOB.INP.001");
    }

    [Fact]
    public void Calculate_WithRequirementsAndNoEvidence_ReturnsZero()
    {
        var skill = EngineTestData.Skill(113, "Rust", "rust");

        var result = Calculate(
            EngineTestData.Resume(),
            EngineTestData.Job(Required(1, skill)));

        Assert.Equal(0m, result.JobMatchScore);
    }

    [Fact]
    public void Calculate_NeverProducesPartialMatch()
    {
        var aspnet = EngineTestData.Skill(
            114,
            "ASP.NET Core",
            "asp.net core");
        var job = EngineTestData.Job(
            Required(1, aspnet),
            Required(2, EngineTestData.Skill(115, "Docker", "docker")));
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                40,
                description: "Built ASP.NET applications")
        ]);

        var result = Calculate(resume, job);

        Assert.DoesNotContain(
            result.KeywordMatches,
            match => match.Status == KeywordMatchStatus.Partial);
    }

    [Fact]
    public void Calculate_KeywordWeightsAlwaysTotalExactlyOneHundred()
    {
        var result = CalculateThreeRequirementGroups();

        Assert.Equal(100.00m, result.KeywordMatches.Sum(match => match.Weight));
    }

    [Fact]
    public void Calculate_JobMatchScoreEqualsMatchedWeightSum()
    {
        var required = EngineTestData.Skill(116, "C#", "c#");
        var preferred = EngineTestData.Skill(117, "Docker", "docker");
        var resume = EngineTestData.Resume(
            skills: [EngineTestData.ResumeSkill(required)]);
        var result = Calculate(
            resume,
            EngineTestData.Job(
                Required(1, required),
                EngineTestData.JobSkill(
                    2,
                    preferred,
                    JobSkillRequirementType.Preferred)));

        var matchedWeight = result.KeywordMatches
            .Where(match => match.Status == KeywordMatchStatus.Matched)
            .Sum(match => match.Weight);

        Assert.Equal(matchedWeight, result.JobMatchScore);
    }

    [Fact]
    public void Calculate_WhenResumeEvidenceChanges_UpdatesJobMatchScore()
    {
        var skill = EngineTestData.Skill(118, "PostgreSQL", "postgresql");
        var job = EngineTestData.Job(Required(1, skill));

        var missing = Calculate(EngineTestData.Resume(), job);
        var matched = Calculate(
            EngineTestData.Resume(
                skills: [EngineTestData.ResumeSkill(skill)]),
            job);

        Assert.Equal(0m, missing.JobMatchScore);
        Assert.Equal(100m, matched.JobMatchScore);
    }

    [Fact]
    public void Match_UnapprovedAiBulletDoesNotCountAsKeywordEvidence()
    {
        var skill = EngineTestData.Skill(119, "Docker", "docker");
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                50,
                position: "Engineer",
                description: null,
                bullets:
                [
                    EngineTestData.Bullet(
                        51,
                        "Built Docker infrastructure",
                        isAiGenerated: true,
                        isUserApproved: false)
                ])
        ]);

        var result = Calculate(
            resume,
            EngineTestData.Job(Required(1, skill)));

        Assert.Equal(
            KeywordMatchStatus.Missing,
            Assert.Single(result.KeywordMatches).Status);
    }

    private JobMatchCalculationResult Calculate(
        ResumeSnapshot resume,
        JobPostingSnapshot job)
    {
        return _calculator.Calculate(resume, job);
    }

    private JobMatchCalculationResult CalculateThreeRequirementGroups()
    {
        var required = EngineTestData.Skill(120, "RequiredSkill");
        var preferred = EngineTestData.Skill(121, "PreferredSkill");
        var mentioned = EngineTestData.Skill(122, "MentionedSkill");

        return Calculate(
            EngineTestData.Resume(),
            EngineTestData.Job(
                Required(1, required),
                EngineTestData.JobSkill(
                    2,
                    preferred,
                    JobSkillRequirementType.Preferred),
                EngineTestData.JobSkill(
                    3,
                    mentioned,
                    JobSkillRequirementType.Mentioned)));
    }

    private static JobPostingSkillSnapshot Required(
        int id,
        SkillSnapshot skill)
    {
        return EngineTestData.JobSkill(
            id,
            skill,
            JobSkillRequirementType.Required);
    }

    private static decimal WeightFor(
        JobMatchCalculationResult result,
        string keyword)
    {
        return result.KeywordMatches.Single(match =>
            match.Keyword == keyword).Weight;
    }
}
