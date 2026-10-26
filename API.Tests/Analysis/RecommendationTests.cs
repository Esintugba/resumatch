using Api.Application.Analysis.Engine;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

public sealed class RecommendationTests
{
    [Fact]
    public void Analyze_MissingSkillRecommendation_DoesNotMutateResumeEvidence()
    {
        var engine = new AnalysisEngineV1();
        var missingSkill = EngineTestData.Skill(200, "Docker", "docker");
        var resume = EngineTestData.Resume(
            skills:
            [
                EngineTestData.ResumeSkill(
                    EngineTestData.Skill(201, "C#", "c#"))
            ]);
        var originalSkills = resume.Skills.ToArray();

        var result = engine.Analyze(EngineTestData.Input(
            resume,
            EngineTestData.Job(EngineTestData.JobSkill(
                1,
                missingSkill,
                JobSkillRequirementType.Required,
                90))));

        Assert.Contains(
            result.Recommendations,
            recommendation => recommendation.RuleCode == "JOB.SKL.001");
        Assert.Equal(originalSkills, resume.Skills.ToArray());
        Assert.DoesNotContain(
            resume.Skills,
            resumeSkill => resumeSkill.Skill.Id == missingSkill.Id);
    }
}
