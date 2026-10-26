using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Engine.Rules;
using Api.Application.Analysis.Engine.Rules.Content;
using Api.Application.Analysis.Engine.Rules.Experience;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

public sealed class RuleBehaviorTests
{
    [Fact]
    public void CategoryScore_ExcludesNotApplicableRuleFromDenominator()
    {
        var metadata = new RuleIssueMetadata(
            AnalysisIssueType.Content,
            IssueSeverity.Warning,
            "Summary");
        var rules = new[]
        {
            new RuleResult(
                "TEST.APPLICABLE",
                RuleCategory.Content,
                20m,
                0.5m,
                metadata),
            new RuleResult(
                "TEST.NOT_APPLICABLE",
                RuleCategory.Content,
                80m,
                null,
                metadata)
        };

        var score = ScoringCalculator.CalculateCategoryScore(
            new CategoryDefinition(RuleCategory.Content, 20m),
            rules);

        Assert.Equal(50m, score);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(39, 0.0)]
    [InlineData(40, 0.5)]
    [InlineData(79, 0.5)]
    [InlineData(80, 1.0)]
    [InlineData(800, 1.0)]
    [InlineData(801, 0.5)]
    [InlineData(1200, 0.5)]
    [InlineData(1201, 0.0)]
    public void SummaryLengthRule_UsesDocumentedThresholds(
        int length,
        double? expected)
    {
        var summary = length == 0 ? null : new string('a', length);
        var result = Evaluate(
            new ContentSummaryLengthRule(),
            EngineTestData.Resume(summary: summary));

        Assert.Equal(expected is null ? null : (decimal?)expected.Value, result);
    }

    [Fact]
    public void ExactDuplicateRule_NormalizesCaseAndWhitespace()
    {
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                10,
                bullets:
                [
                    EngineTestData.Bullet(11, "Built   reliable APIs"),
                    EngineTestData.Bullet(12, " built reliable APIs ")
                ])
        ]);

        var result = Evaluate(new ContentExactDuplicateRule(), resume);

        Assert.Equal(0m, result);
    }

    [Theory]
    [InlineData("TODO", 0.0)]
    [InlineData("  lorem   ipsum  ", 0.0)]
    [InlineData("Implemented TODO workflow for the editor", 1.0)]
    [InlineData("TBD integration was completed", 1.0)]
    [InlineData("XXX is a valid project codename", 1.0)]
    public void PlaceholderRule_OnlyFlagsExactVersionedValues(
        string content,
        double expected)
    {
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                20,
                bullets: [EngineTestData.Bullet(21, content)])
        ]);

        var result = Evaluate(new ContentPlaceholderRule(), resume);

        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void UnapprovedAiBullet_IsNotUsableExperienceEvidence()
    {
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                30,
                description: null,
                bullets:
                [
                    EngineTestData.Bullet(
                        31,
                        "Built production Docker infrastructure.",
                        isAiGenerated: true,
                        isUserApproved: false)
                ])
        ]);

        var approvalResult = Evaluate(
            new ContentApprovedBulletRule(),
            resume);
        var evidenceResult = Evaluate(
            new ExperienceEvidenceCoverageRule(),
            resume);

        Assert.Equal(0m, approvalResult);
        Assert.Equal(0m, evidenceResult);
    }

    [Theory]
    [InlineData("Migrated services to .NET 8")]
    [InlineData("Adopted C# 12 language features")]
    [InlineData("Handled HTTP 200 responses")]
    [InlineData("Completed platform migration in 2025")]
    public void QuantificationRule_DoesNotTreatTechnologyVersionsOrYearsAsOutcomes(
        string bullet)
    {
        var resume = EngineTestData.Resume(experiences:
        [
            EngineTestData.Experience(
                40,
                bullets: [EngineTestData.Bullet(41, bullet)])
        ]);

        var result = Evaluate(new ExperienceQuantificationRule(), resume);

        Assert.Equal(0m, result);
    }

    private static decimal? Evaluate(
        IAnalysisRule rule,
        ResumeSnapshot resume)
    {
        return rule.Evaluate(new RuleEvaluationContext(
            EngineTestData.Input(resume)));
    }
}
