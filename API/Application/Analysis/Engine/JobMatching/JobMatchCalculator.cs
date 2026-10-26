using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Keywords;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.JobMatching;

public sealed class JobMatchCalculator : IJobMatchCalculator
{
    private readonly IKeywordMatcher _keywordMatcher;

    public JobMatchCalculator(IKeywordMatcher keywordMatcher)
    {
        _keywordMatcher = keywordMatcher ??
            throw new ArgumentNullException(nameof(keywordMatcher));
    }

    public JobMatchCalculationResult Calculate(
        ResumeSnapshot resume,
        JobPostingSnapshot jobPosting)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(jobPosting);

        var requirements = JobSkillRequirementSelector.Select(jobPosting);
        if (requirements.IsDefaultOrEmpty)
        {
            return new JobMatchCalculationResult(
                null,
                ImmutableArray<KeywordMatchResult>.Empty,
                [CreateMissingRequirementsIssue()]);
        }

        var keywordMatches = _keywordMatcher.Match(resume, jobPosting);
        ValidateKeywordMatches(requirements, keywordMatches);

        var matchesBySkillId = keywordMatches.ToDictionary(
            match => match.SkillId!.Value);
        var issues = ImmutableArray.CreateBuilder<AnalysisIssueResult>();

        foreach (var requirement in requirements)
        {
            var match = matchesBySkillId[requirement.Skill.Skill.Id];
            if (match.Status != KeywordMatchStatus.Missing)
            {
                continue;
            }

            var issue = CreateMissingSkillIssue(requirement, match.Keyword);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        var jobMatchScore = keywordMatches
            .Where(match => match.Status == KeywordMatchStatus.Matched)
            .Sum(match => match.Weight);

        return new JobMatchCalculationResult(
            jobMatchScore,
            keywordMatches,
            issues.ToImmutable());
    }

    private static void ValidateKeywordMatches(
        ImmutableArray<CanonicalJobSkillRequirement> requirements,
        ImmutableArray<KeywordMatchResult> keywordMatches)
    {
        if (keywordMatches.IsDefault ||
            keywordMatches.Length != requirements.Length)
        {
            throw new InvalidOperationException(
                "Keyword matcher must return exactly one result per canonical requirement.");
        }

        if (keywordMatches.Any(match =>
                !match.SkillId.HasValue ||
                match.Status == KeywordMatchStatus.Partial ||
                !Enum.IsDefined(match.Status)))
        {
            throw new InvalidOperationException(
                "Keyword matcher returned an unsupported v1 result.");
        }

        var expectedSkillIds = requirements
            .Select(requirement => requirement.Skill.Skill.Id)
            .ToHashSet();
        var actualSkillIds = keywordMatches
            .Select(match => match.SkillId!.Value)
            .ToArray();

        if (actualSkillIds.Distinct().Count() != actualSkillIds.Length ||
            !expectedSkillIds.SetEquals(actualSkillIds))
        {
            throw new InvalidOperationException(
                "Keyword matcher results do not match canonical requirements.");
        }

        if (keywordMatches.Sum(match => match.Weight) != 100m)
        {
            throw new InvalidOperationException(
                "Keyword match weights must total exactly 100.00.");
        }
    }

    private static AnalysisIssueResult CreateMissingRequirementsIssue()
    {
        return new AnalysisIssueResult(
            AnalysisIssueType.Keyword,
            IssueSeverity.Warning,
            "JOB.INP.001",
            "Job requirements are unavailable.",
            "The job posting has no skill requirements, so a job match score " +
            "cannot be calculated.",
            null);
    }

    private static AnalysisIssueResult? CreateMissingSkillIssue(
        CanonicalJobSkillRequirement requirement,
        string keyword)
    {
        var importanceScore = requirement.Skill.ImportanceScore;

        return requirement.Skill.RequirementType switch
        {
            JobSkillRequirementType.Required => new AnalysisIssueResult(
                AnalysisIssueType.Keyword,
                importanceScore >= 70
                    ? IssueSeverity.Critical
                    : IssueSeverity.Warning,
                "JOB.SKL.001",
                $"Missing required skill: {keyword}",
                $"The required skill '{keyword}' was not found in the resume.",
                "Skills"),

            JobSkillRequirementType.Preferred => new AnalysisIssueResult(
                AnalysisIssueType.Keyword,
                importanceScore >= 70
                    ? IssueSeverity.Warning
                    : IssueSeverity.Info,
                "JOB.SKL.002",
                $"Missing preferred skill: {keyword}",
                $"The preferred skill '{keyword}' was not found in the resume.",
                "Skills"),

            JobSkillRequirementType.Mentioned => null,

            _ => throw new ArgumentOutOfRangeException(
                nameof(requirement),
                "Job skill requirement type is not defined.")
        };
    }
}
