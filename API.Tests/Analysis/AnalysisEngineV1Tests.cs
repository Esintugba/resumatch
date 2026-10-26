using System.Collections.Immutable;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

public sealed class AnalysisEngineV1Tests
{
    private readonly AnalysisEngineV1 _engine = new();

    [Fact]
    public void Analyze_WithSameInput_ProducesIdenticalResult()
    {
        var dotnet = EngineTestData.Skill(10, ".NET", ".net");
        var input = EngineTestData.Input(
            EngineTestData.Resume(
                summary: new string('a', 100),
                contact: EngineTestData.Contact(),
                skills: [EngineTestData.ResumeSkill(dotnet)],
                experiences:
                [
                    EngineTestData.Experience(
                        20,
                        bullets:
                        [
                            EngineTestData.Bullet(
                                21,
                                "Improved API throughput by 30 percent.")
                        ])
                ]),
            EngineTestData.Job(
                EngineTestData.JobSkill(
                    1,
                    dotnet,
                    JobSkillRequirementType.Required)));

        var first = _engine.Analyze(input);
        var second = _engine.Analyze(input);

        AssertResultsEqual(first, second);
    }

    [Fact]
    public void Analyze_WhenCollectionOrderChanges_PreservesScores()
    {
        var csharp = EngineTestData.Skill(11, "C#", "c#");
        var dotnet = EngineTestData.Skill(12, ".NET", ".net");
        var firstExperience = EngineTestData.Experience(
            30,
            sortOrder: 0,
            bullets: [EngineTestData.Bullet(31, "Built reliable C# services.")]);
        var secondExperience = EngineTestData.Experience(
            32,
            sortOrder: 1,
            bullets: [EngineTestData.Bullet(33, "Built APIs with .NET.")]);

        var ordered = EngineTestData.Resume(
            contact: EngineTestData.Contact(),
            skills:
            [
                EngineTestData.ResumeSkill(csharp, 0),
                EngineTestData.ResumeSkill(dotnet, 1)
            ],
            experiences: [firstExperience, secondExperience]);
        var reversed = ordered with
        {
            Skills = ordered.Skills.Reverse().ToImmutableArray(),
            Experiences = ordered.Experiences.Reverse().ToImmutableArray()
        };

        var first = _engine.Analyze(EngineTestData.Input(ordered));
        var second = _engine.Analyze(EngineTestData.Input(reversed));

        Assert.Equal(first.AtsScore, second.AtsScore);
        Assert.Equal(first.StructureScore, second.StructureScore);
        Assert.Equal(first.FormattingScore, second.FormattingScore);
        Assert.Equal(first.ContentScore, second.ContentScore);
        Assert.Equal(first.KeywordScore, second.KeywordScore);
    }

    [Fact]
    public void Analyze_EmptyResume_ProducesScoresWithinPersistedRange()
    {
        var result = _engine.Analyze(
            EngineTestData.Input(EngineTestData.Resume()));

        AssertAllScoresInRange(result);
    }

    [Fact]
    public void Analyze_WithoutContact_ProducesCriticalContactIssue()
    {
        var result = _engine.Analyze(
            EngineTestData.Input(EngineTestData.Resume()));

        var issue = Assert.Single(
            result.Issues.Where(issue => issue.Code == "ATS.CON.001"));
        Assert.Equal(IssueSeverity.Critical, issue.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Analyze_WithMissingOrInvalidEmail_ProducesCriticalIssue(
        string email)
    {
        var result = _engine.Analyze(EngineTestData.Input(
            EngineTestData.Resume(contact: EngineTestData.Contact(email))));

        var issue = Assert.Single(
            result.Issues.Where(issue => issue.Code == "ATS.CON.003"));
        Assert.Equal(IssueSeverity.Critical, issue.Severity);
    }

    [Fact]
    public void Analyze_WithDifferentJobs_PreservesAtsScore()
    {
        var resume = EngineTestData.Resume(
            contact: EngineTestData.Contact(),
            skills:
            [
                EngineTestData.ResumeSkill(
                    EngineTestData.Skill(40, "C#", "c#"))
            ]);
        var csharpJob = EngineTestData.Job(EngineTestData.JobSkill(
            1,
            EngineTestData.Skill(40, "C#", "c#"),
            JobSkillRequirementType.Required));
        var goJob = EngineTestData.Job(EngineTestData.JobSkill(
            2,
            EngineTestData.Skill(41, "Go", "go"),
            JobSkillRequirementType.Required));

        var withoutJob = _engine.Analyze(EngineTestData.Input(resume));
        var withCSharp = _engine.Analyze(EngineTestData.Input(resume, csharpJob));
        var withGo = _engine.Analyze(EngineTestData.Input(resume, goJob));

        Assert.Equal(withoutJob.AtsScore, withCSharp.AtsScore);
        Assert.Equal(withoutJob.AtsScore, withGo.AtsScore);
        Assert.Equal(withoutJob.StructureScore, withCSharp.StructureScore);
        Assert.Equal(withoutJob.ContentScore, withGo.ContentScore);
    }

    [Fact]
    public void Analyze_AlwaysProducesPersistableScoresAndV1Version()
    {
        var result = _engine.Analyze(EngineTestData.Input(
            EngineTestData.Resume(contact: EngineTestData.Contact())));

        AssertAllScoresInRange(result);
        Assert.Equal("ats-v1", result.AnalyzerVersion);
    }

    private static void AssertAllScoresInRange(AnalysisResult result)
    {
        Assert.InRange(result.AtsScore, 0m, 100m);
        Assert.InRange(result.StructureScore, 0m, 100m);
        Assert.InRange(result.FormattingScore, 0m, 100m);
        Assert.InRange(result.ContentScore, 0m, 100m);
        Assert.InRange(result.KeywordScore, 0m, 100m);
        if (result.JobMatchScore.HasValue)
        {
            Assert.InRange(result.JobMatchScore.Value, 0m, 100m);
        }

        Assert.All(result.CategoryScores, score =>
            Assert.InRange(score.Score, 0m, 100m));
    }

    private static void AssertResultsEqual(
        AnalysisResult expected,
        AnalysisResult actual)
    {
        Assert.Equal(expected.AtsScore, actual.AtsScore);
        Assert.Equal(expected.JobMatchScore, actual.JobMatchScore);
        Assert.Equal(expected.StructureScore, actual.StructureScore);
        Assert.Equal(expected.FormattingScore, actual.FormattingScore);
        Assert.Equal(expected.ContentScore, actual.ContentScore);
        Assert.Equal(expected.KeywordScore, actual.KeywordScore);
        Assert.Equal(expected.AnalyzerVersion, actual.AnalyzerVersion);
        Assert.Equal(expected.CategoryScores.ToArray(), actual.CategoryScores.ToArray());
        Assert.Equal(expected.RuleResults.ToArray(), actual.RuleResults.ToArray());
        Assert.Equal(expected.Issues.ToArray(), actual.Issues.ToArray());
        Assert.Equal(expected.KeywordMatches.ToArray(), actual.KeywordMatches.ToArray());
        Assert.Equal(expected.Recommendations.ToArray(), actual.Recommendations.ToArray());
    }
}
