using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Engine.Rules;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Recommendations;

public sealed class RecommendationFactoryV1 : IRecommendationFactory
{
    private const int MaximumRecommendationCount = 15;

    public ImmutableArray<AnalysisRecommendationResult> Create(
        IEnumerable<RuleResult> ruleResults,
        IEnumerable<AnalysisIssueResult> issues,
        IEnumerable<KeywordMatchResult> keywordMatches)
    {
        ArgumentNullException.ThrowIfNull(ruleResults);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(keywordMatches);

        var ruleArray = ruleResults.ToArray();
        var issueArray = issues.ToArray();
        var keywordMatchArray = keywordMatches.ToArray();
        ValidateInputs(ruleArray, issueArray, keywordMatchArray);

        var potentialImpacts = CalculatePotentialImpacts(ruleArray);
        var jobMatchImpacts = CalculateJobMatchImpacts(
            issueArray,
            keywordMatchArray);
        var candidates = issueArray.Select((issue, issueIndex) =>
        {
            var template = GetTemplate(issue);
            var priority = MapPriority(issue.Severity);
            var impact = Math.Max(
                potentialImpacts.GetValueOrDefault(issue.Code, 0m),
                jobMatchImpacts.GetValueOrDefault(issueIndex, 0m));

            return new RecommendationCandidate(
                new AnalysisRecommendationResult(
                    issue.Code,
                    template.Type,
                    priority,
                    template.Title,
                    template.Description,
                    issue.ResumeSection,
                    null,
                    null,
                    false,
                    RecommendationStatus.Pending),
                impact);
        });

        return candidates
            .GroupBy(candidate => new RecommendationKey(
                candidate.Recommendation.Type,
                candidate.Recommendation.TargetSection,
                candidate.Recommendation.RuleCode))
            .Select(group => group
                .OrderByDescending(candidate =>
                    GetPriorityRank(candidate.Recommendation.Priority))
                .ThenByDescending(candidate => candidate.PotentialScoreImpact)
                .ThenBy(candidate => candidate.Recommendation.Title,
                    StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Recommendation.Description,
                    StringComparer.Ordinal)
                .First())
            .OrderByDescending(candidate =>
                GetPriorityRank(candidate.Recommendation.Priority))
            .ThenByDescending(candidate => candidate.PotentialScoreImpact)
            .ThenBy(candidate => candidate.Recommendation.RuleCode,
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Recommendation.Type)
            .ThenBy(candidate => candidate.Recommendation.TargetSection,
                StringComparer.Ordinal)
            .Take(MaximumRecommendationCount)
            .Select(candidate => candidate.Recommendation)
            .ToImmutableArray();
    }

    private static Dictionary<string, decimal> CalculatePotentialImpacts(
        IReadOnlyCollection<RuleResult> rules)
    {
        var applicableWeightByCategory = rules
            .Where(rule => rule.IsApplicable)
            .GroupBy(rule => rule.Category)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(rule => rule.RuleWeight));

        return rules
            .Where(rule => rule.ShouldCreateIssue)
            .ToDictionary(
                rule => rule.RuleCode,
                rule =>
                {
                    var applicableWeight = applicableWeightByCategory[
                        rule.Category];
                    var categoryWeight = GetAtsCategoryWeight(rule.Category);
                    var resultGap = 1m - rule.Result!.Value;

                    return categoryWeight *
                           rule.RuleWeight /
                           applicableWeight *
                           resultGap;
                },
                StringComparer.Ordinal);
    }

    private static decimal GetAtsCategoryWeight(RuleCategory category)
    {
        return category switch
        {
            RuleCategory.Structure => AtsCategoryDefinitions.StructureWeight,
            RuleCategory.Formatting => AtsCategoryDefinitions.FormattingWeight,
            RuleCategory.Contact => AtsCategoryDefinitions.ContactWeight,
            RuleCategory.Content => AtsCategoryDefinitions.ContentWeight,
            RuleCategory.Skills => AtsCategoryDefinitions.SkillsWeight,
            RuleCategory.Experience => AtsCategoryDefinitions.ExperienceWeight,
            RuleCategory.JobMatch => 0m,
            _ => throw new ArgumentOutOfRangeException(
                nameof(category),
                "Rule category is not defined.")
        };
    }

    private static Dictionary<int, decimal> CalculateJobMatchImpacts(
        IReadOnlyList<AnalysisIssueResult> issues,
        IReadOnlyCollection<KeywordMatchResult> keywordMatches)
    {
        var jobSkillIssueIndexes = issues
            .Select((issue, index) => new { Issue = issue, Index = index })
            .Where(item => item.Issue.Code is "JOB.SKL.001" or "JOB.SKL.002")
            .Select(item => item.Index)
            .ToArray();

        var missingKeywordMatches = keywordMatches
            .Where(match => match.Status == KeywordMatchStatus.Missing)
            .ToArray();

        if (jobSkillIssueIndexes.Length > missingKeywordMatches.Length)
        {
            throw new InvalidOperationException(
                "Missing-skill issues exceed missing keyword match results.");
        }

        var impacts = new Dictionary<int, decimal>();
        for (var index = 0; index < jobSkillIssueIndexes.Length; index++)
        {
            impacts[jobSkillIssueIndexes[index]] =
                missingKeywordMatches[index].Weight;
        }

        return impacts;
    }

    private static RecommendationPriority MapPriority(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Critical => RecommendationPriority.High,
            IssueSeverity.Warning => RecommendationPriority.Medium,
            IssueSeverity.Info => RecommendationPriority.Low,
            _ => throw new ArgumentOutOfRangeException(
                nameof(severity),
                "Issue severity is not defined.")
        };
    }

    private static int GetPriorityRank(RecommendationPriority priority)
    {
        return priority switch
        {
            RecommendationPriority.High => 3,
            RecommendationPriority.Medium => 2,
            RecommendationPriority.Low => 1,
            _ => throw new ArgumentOutOfRangeException(
                nameof(priority),
                "Recommendation priority is not defined.")
        };
    }

    private static RecommendationTemplate GetTemplate(
        AnalysisIssueResult issue)
    {
        return issue.Code switch
        {
            "ATS.STR.001" => new(
                RecommendationType.Structure,
                "Add a core resume section",
                "Add at least one truthful Experience, Education, or Project " +
                "entry that reflects your actual background."),

            "ATS.STR.002" => new(
                RecommendationType.Structure,
                "Clarify your professional direction",
                "Add a professional title or target role that accurately " +
                "describes the position you are pursuing."),

            "ATS.STR.003" => new(
                RecommendationType.Structure,
                "Review resume dates",
                "Correct inconsistent date ranges and keep the end date empty " +
                "for current experience entries."),

            "ATS.STR.004" => new(
                RecommendationType.Structure,
                "Make section ordering deterministic",
                "Use non-negative, non-duplicated ordering values within each " +
                "resume section."),

            "ATS.CON.001" => new(
                RecommendationType.Structure,
                "Add contact information",
                "Add a contact section containing only accurate information " +
                "you are comfortable sharing with employers."),

            "ATS.CON.002" => new(
                RecommendationType.Structure,
                "Complete your name",
                "Provide both your first and last name in the contact section."),

            "ATS.CON.003" => new(
                RecommendationType.Structure,
                "Provide a valid email address",
                "Review the contact email and replace it with a valid address " +
                "that you control."),

            "ATS.CON.004" => new(
                RecommendationType.Other,
                "Add a general location",
                "If appropriate, add your city or country without including " +
                "unnecessary sensitive address details."),

            "ATS.CON.005" => new(
                RecommendationType.Other,
                "Add a professional contact channel",
                "If available, add your real phone number or a professional " +
                "profile or portfolio link."),

            "ATS.CNT.001" => new(
                RecommendationType.Summary,
                "Add a professional summary",
                "Write a concise summary based only on your actual skills, " +
                "experience, and career focus."),

            "ATS.CNT.002" => new(
                RecommendationType.Summary,
                "Adjust summary length",
                "Keep the summary focused and within the recommended length " +
                "without adding unsupported claims."),

            "ATS.CNT.003" => new(
                RecommendationType.Experience,
                "Add evidence to resume entries",
                "Describe completed work in Experience or Project entries using " +
                "only facts from your actual background."),

            "ATS.CNT.004" => new(
                RecommendationType.Experience,
                "Remove repeated content",
                "Replace exact duplicate bullets or project descriptions with " +
                "distinct, truthful details."),

            "ATS.CNT.005" => new(
                RecommendationType.Other,
                "Replace placeholder text",
                "Remove temporary placeholder values and enter finalized, " +
                "accurate content."),

            "ATS.CNT.006" => new(
                RecommendationType.Experience,
                "Review AI-generated bullets",
                "Verify every AI-generated bullet against your real experience " +
                "and approve it only when it is accurate."),

            "ATS.EXP.001" => new(
                RecommendationType.Experience,
                "Add relevant experience evidence",
                "Add a truthful Experience or Project entry when it reflects " +
                "work you actually completed."),

            "ATS.EXP.002" => new(
                RecommendationType.Experience,
                "Describe each experience",
                "Add at least one accurate bullet or description to each " +
                "experience entry."),

            "ATS.EXP.003" => new(
                RecommendationType.Experience,
                "Improve bullet length",
                "Keep experience bullets concise enough to scan while retaining " +
                "the factual context needed to understand the work."),

            "ATS.EXP.004" => new(
                RecommendationType.Quantification,
                "Add verified outcomes where available",
                "Where you have a real measured result, state it accurately. " +
                "Do not invent achievements or metrics."),

            "ATS.EXP.005" => new(
                RecommendationType.Experience,
                "Complete project evidence",
                "For each real project, add an accurate description and select " +
                "only skills that were actually used."),

            "ATS.EXP.006" => new(
                RecommendationType.Experience,
                "Balance experience bullets",
                "Use a focused number of factual bullets for each experience " +
                "instead of leaving it empty or overloading it."),

            "ATS.FMT.001" => new(
                RecommendationType.Formatting,
                "Remove unsupported control characters",
                "Remove hidden control characters while preserving normal line " +
                "breaks and tabs where needed."),

            "ATS.FMT.002" => new(
                RecommendationType.Formatting,
                "Remove HTML-like markup",
                "Replace HTML or script-like tags with plain resume text."),

            "ATS.FMT.003" => new(
                RecommendationType.Formatting,
                "Simplify bullet formatting",
                "Keep each bullet as one clear item and remove excessive line " +
                "breaks or pasted nested lists."),

            "ATS.FMT.004" => new(
                RecommendationType.Formatting,
                "Correct resume URLs",
                "Use complete HTTP or HTTPS addresses for the links you choose " +
                "to include."),

            "ATS.SKL.001" => new(
                RecommendationType.Skill,
                "Review the explicit skills list",
                "List only skills you genuinely possess and keep the list " +
                "focused on relevant capabilities."),

            "ATS.SKL.002" => new(
                RecommendationType.Skill,
                "Clean up canonical skill entries",
                "Use normalized, categorized skill entries and remove canonical " +
                "duplicates."),

            "ATS.SKL.003" => new(
                RecommendationType.Skill,
                "Align project and explicit skills",
                "If a project skill represents a skill you genuinely possess, " +
                "also represent it consistently in the Skills section."),

            "JOB.INP.001" => new(
                RecommendationType.Keyword,
                "Analyze job requirements",
                "Add or parse job skill requirements before relying on a job " +
                "match score."),

            "JOB.SKL.001" => new(
                RecommendationType.Skill,
                "Review missing required skill evidence",
                "This skill is required by the job posting. If you genuinely " +
                "have it, document it with verifiable evidence in Skills, " +
                "Projects, or Experience. Do not add it otherwise."),

            "JOB.SKL.002" => new(
                RecommendationType.Skill,
                "Review missing preferred skill evidence",
                "This skill is preferred by the job posting. If you genuinely " +
                "have it, document it with verifiable evidence in Skills, " +
                "Projects, or Experience. Do not add it otherwise."),

            _ => CreateFallbackTemplate(issue)
        };
    }

    private static RecommendationTemplate CreateFallbackTemplate(
        AnalysisIssueResult issue)
    {
        var type = issue.Type switch
        {
            AnalysisIssueType.Formatting => RecommendationType.Formatting,
            AnalysisIssueType.Structure => RecommendationType.Structure,
            AnalysisIssueType.Content => RecommendationType.Other,
            AnalysisIssueType.Keyword => RecommendationType.Keyword,
            AnalysisIssueType.ContactInformation => RecommendationType.Structure,
            AnalysisIssueType.Experience => RecommendationType.Experience,
            AnalysisIssueType.Education => RecommendationType.Structure,
            AnalysisIssueType.Skill => RecommendationType.Skill,
            AnalysisIssueType.Other => RecommendationType.Other,
            _ => throw new ArgumentOutOfRangeException(
                nameof(issue),
                "Analysis issue type is not defined.")
        };

        return new RecommendationTemplate(
            type,
            issue.Title,
            $"Review and resolve this analysis issue: {issue.Description}");
    }

    private static void ValidateInputs(
        IReadOnlyCollection<RuleResult> rules,
        IReadOnlyCollection<AnalysisIssueResult> issues,
        IReadOnlyCollection<KeywordMatchResult> keywordMatches)
    {
        if (rules.Any(rule => rule is null))
        {
            throw new ArgumentException(
                "Rule results cannot contain null values.",
                nameof(rules));
        }

        var duplicateRuleCode = rules
            .GroupBy(rule => rule.RuleCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateRuleCode is not null)
        {
            throw new InvalidOperationException(
                $"Rule result '{duplicateRuleCode}' is duplicated.");
        }

        if (issues.Any(issue => issue is null))
        {
            throw new ArgumentException(
                "Analysis issues cannot contain null values.",
                nameof(issues));
        }

        if (keywordMatches.Any(match => match is null))
        {
            throw new ArgumentException(
                "Keyword matches cannot contain null values.",
                nameof(keywordMatches));
        }
    }

    private sealed record RecommendationTemplate(
        RecommendationType Type,
        string Title,
        string Description);

    private sealed record RecommendationCandidate(
        AnalysisRecommendationResult Recommendation,
        decimal PotentialScoreImpact);

    private sealed record RecommendationKey(
        RecommendationType Type,
        string? TargetSection,
        string RuleCode);
}
