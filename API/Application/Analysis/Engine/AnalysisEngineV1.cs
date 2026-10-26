using System.Collections.Immutable;
using Api.Application.Analysis.Engine.JobMatching;
using Api.Application.Analysis.Engine.Keywords;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Engine.Recommendations;
using Api.Application.Analysis.Engine.Rules;
using Api.Application.Analysis.Engine.Rules.Contact;
using Api.Application.Analysis.Engine.Rules.Content;
using Api.Application.Analysis.Engine.Rules.Experience;
using Api.Application.Analysis.Engine.Rules.Formatting;
using Api.Application.Analysis.Engine.Rules.Skills;
using Api.Application.Analysis.Engine.Rules.Structure;

namespace Api.Application.Analysis.Engine;

public sealed class AnalysisEngineV1 : IAnalysisEngine
{
    public const string Version = "ats-v1";

    private static readonly ImmutableArray<IAnalysisRule> StructureRules =
    [
        new StructureSectionPresenceRule(),
        new StructureProfessionalHeadingRule(),
        new StructureDateConsistencyRule(),
        new StructureSortOrderRule()
    ];

    private static readonly ImmutableArray<IAnalysisRule> FormattingRules =
    [
        new FormattingControlCharacterRule(),
        new FormattingHtmlTagRule(),
        new FormattingBulletTextRule(),
        new FormattingUrlRule()
    ];

    private static readonly ImmutableArray<IAnalysisRule> ContactRules =
    [
        new ContactPresenceRule(),
        new ContactNameRule(),
        new ContactEmailRule(),
        new ContactLocationRule(),
        new ContactChannelRule()
    ];

    private static readonly ImmutableArray<IAnalysisRule> ContentRules =
    [
        new ContentSummaryPresenceRule(),
        new ContentSummaryLengthRule(),
        new ContentRecordCoverageRule(),
        new ContentExactDuplicateRule(),
        new ContentPlaceholderRule(),
        new ContentApprovedBulletRule()
    ];

    private static readonly ImmutableArray<IAnalysisRule> SkillsRules =
    [
        new SkillsExplicitCountRule(),
        new SkillsCanonicalQualityRule(),
        new SkillsProjectRepresentationRule()
    ];

    private static readonly ImmutableArray<IAnalysisRule> ExperienceRules =
    [
        new ExperienceOrProjectPresenceRule(),
        new ExperienceEvidenceCoverageRule(),
        new ExperienceBulletLengthRule(),
        new ExperienceQuantificationRule(),
        new ExperienceProjectCompletenessRule(),
        new ExperienceBulletCountRule()
    ];

    private static readonly IJobMatchCalculator JobMatchCalculator =
        new JobMatchCalculator(new ExactKeywordMatcher());

    private static readonly IRecommendationFactory RecommendationFactory =
        new RecommendationFactoryV1();

    public string AnalyzerVersion => Version;

    public AnalysisResult Analyze(AnalysisInput input)
    {
        ValidateInput(input);

        // ATS v1 deliberately receives no JobPosting data. This preserves the
        // invariant that ATS scores depend only on the same resume snapshot.
        var atsContext = new RuleEvaluationContext(new AnalysisInput(
            input.Resume,
            null,
            input.AsOf));

        var ruleResults = ImmutableArray.CreateBuilder<RuleResult>();
        ExecuteRules(StructureRules, atsContext, ruleResults);
        ExecuteRules(FormattingRules, atsContext, ruleResults);
        ExecuteRules(ContactRules, atsContext, ruleResults);
        ExecuteRules(ContentRules, atsContext, ruleResults);
        ExecuteRules(SkillsRules, atsContext, ruleResults);
        ExecuteRules(ExperienceRules, atsContext, ruleResults);

        var finalizedRuleResults = ruleResults.ToImmutable();
        var categoryScores = ScoringCalculator.CalculateCategoryScores(
            AtsCategoryDefinitions.All,
            finalizedRuleResults);
        var atsCalculation = ScoringCalculator.CalculateAtsScore(categoryScores);

        var ruleIssues = CreateRuleIssues(finalizedRuleResults);
        var jobMatch = input.JobPosting is null
            ? new JobMatchCalculationResult(
                null,
                ImmutableArray<KeywordMatchResult>.Empty,
                ImmutableArray<AnalysisIssueResult>.Empty)
            : JobMatchCalculator.Calculate(input.Resume, input.JobPosting);

        var issues = ruleIssues.AddRange(jobMatch.Issues);
        var recommendations = RecommendationFactory.Create(
            finalizedRuleResults,
            issues,
            jobMatch.KeywordMatches);

        return new AnalysisResult(
            atsCalculation.AtsScore,
            jobMatch.JobMatchScore,
            atsCalculation.StructureScore,
            atsCalculation.FormattingScore,
            atsCalculation.ContentScore,
            atsCalculation.KeywordScore,
            Version,
            atsCalculation.CategoryScores,
            finalizedRuleResults,
            issues,
            jobMatch.KeywordMatches,
            recommendations);
    }

    private static void ExecuteRules(
        IEnumerable<IAnalysisRule> rules,
        RuleEvaluationContext context,
        ICollection<RuleResult> target)
    {
        foreach (var result in RuleExecutor.Execute(rules, context))
        {
            target.Add(result);
        }
    }

    private static ImmutableArray<AnalysisIssueResult> CreateRuleIssues(
        IEnumerable<RuleResult> ruleResults)
    {
        return ruleResults
            .Where(result => result.ShouldCreateIssue)
            .Select(result =>
            {
                var text = GetIssueText(result.RuleCode);
                return new AnalysisIssueResult(
                    result.FailureIssue.Type,
                    result.FailureIssue.Severity,
                    result.RuleCode,
                    text.Title,
                    text.Description,
                    result.FailureIssue.ResumeSection);
            })
            .ToImmutableArray();
    }

    private static IssueText GetIssueText(string ruleCode)
    {
        return ruleCode switch
        {
            "ATS.STR.001" => new(
                "Core resume sections are missing.",
                "The resume has no Experience, Education, or Project entries."),
            "ATS.STR.002" => new(
                "Professional direction is missing.",
                "Neither a professional title nor a target role is present."),
            "ATS.STR.003" => new(
                "Some resume dates are inconsistent.",
                "One or more dated records have an invalid date relationship."),
            "ATS.STR.004" => new(
                "Section ordering is inconsistent.",
                "One or more populated sections contain invalid or duplicate ordering values."),

            "ATS.FMT.001" => new(
                "Problematic control characters were detected.",
                "Resume text contains control characters other than normal line breaks or tabs."),
            "ATS.FMT.002" => new(
                "HTML-like markup was detected.",
                "Resume text contains HTML or script-like tags that may be unsafe for ATS parsing."),
            "ATS.FMT.003" => new(
                "Some bullets have unsafe text formatting.",
                "One or more experience bullets contain excessive line breaks or pasted list structures."),
            "ATS.FMT.004" => new(
                "Some resume links are invalid.",
                "One or more supplied links are not absolute HTTP or HTTPS URLs."),

            "ATS.CON.001" => new(
                "Contact information is missing.",
                "The resume does not contain a contact section."),
            "ATS.CON.002" => new(
                "Contact name is incomplete.",
                "The contact section does not contain both a first and last name."),
            "ATS.CON.003" => new(
                "Contact email is invalid.",
                "The contact section does not contain a valid email address."),
            "ATS.CON.004" => new(
                "General location is missing.",
                "The contact section contains neither a city nor a country."),
            "ATS.CON.005" => new(
                "Professional contact channel is missing.",
                "No phone number or supported professional link was found."),

            "ATS.CNT.001" => new(
                "Professional summary is missing.",
                "The resume does not contain a professional summary."),
            "ATS.CNT.002" => new(
                "Summary length is outside the preferred range.",
                "The professional summary is too short or too long for the v1 content rule."),
            "ATS.CNT.003" => new(
                "Some entries lack supporting content.",
                "One or more Experience or Project entries have no usable description or bullet evidence."),
            "ATS.CNT.004" => new(
                "Exact duplicate content was detected.",
                "The resume contains repeated bullet or project-description text."),
            "ATS.CNT.005" => new(
                "Placeholder content was detected.",
                "The resume contains an exact placeholder value from the v1 placeholder catalog."),
            "ATS.CNT.006" => new(
                "Some AI-generated bullets are unapproved.",
                "One or more AI-generated experience bullets have not been approved by the user."),

            "ATS.SKL.001" => new(
                "Explicit skill coverage can be improved.",
                "The number of distinct explicit skills is outside the preferred v1 range."),
            "ATS.SKL.002" => new(
                "Some skill entries are not canonical.",
                "One or more skills lack a normalized name, valid category, or canonical uniqueness."),
            "ATS.SKL.003" => new(
                "Project skills are not fully represented.",
                "One or more project skills are absent from the explicit Skills section."),

            "ATS.EXP.001" => new(
                "Experience evidence is missing.",
                "The resume contains neither Experience nor Project entries."),
            "ATS.EXP.002" => new(
                "Some experiences lack evidence.",
                "One or more experience entries have no usable bullet or description."),
            "ATS.EXP.003" => new(
                "Some bullet lengths are outside the preferred range.",
                "One or more usable experience bullets are too short or too long."),
            "ATS.EXP.004" => new(
                "Verified outcomes are limited.",
                "Some usable experience bullets do not contain a conservatively detected quantified outcome."),
            "ATS.EXP.005" => new(
                "Some projects are incomplete.",
                "One or more projects lack a description or an associated project skill."),
            "ATS.EXP.006" => new(
                "Experience bullet counts can be improved.",
                "One or more experience entries have too few or too many usable bullets."),

            _ => throw new InvalidOperationException(
                $"ATS v1 has no issue text for rule '{ruleCode}'.")
        };
    }

    private static void ValidateInput(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Resume.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "Resume identifier cannot be empty.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Resume.Title))
        {
            throw new ArgumentException(
                "Resume title is required.",
                nameof(input));
        }

        if (input.AsOf == default)
        {
            throw new ArgumentException(
                "Analysis AsOf timestamp is required.",
                nameof(input));
        }

        ValidateResumeGraph(input.Resume);

        if (input.JobPosting is not null)
        {
            if (input.JobPosting.Id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Job posting identifier cannot be empty.",
                    nameof(input));
            }

            ValidateCollection(input.JobPosting.Skills, "JobPosting.Skills");
            foreach (var jobSkill in input.JobPosting.Skills.OrEmpty())
            {
                ArgumentNullException.ThrowIfNull(jobSkill.Skill);
            }
        }
    }

    private static void ValidateResumeGraph(ResumeSnapshot resume)
    {
        ValidateCollection(resume.Links, "Resume.Links");
        ValidateCollection(resume.Experiences, "Resume.Experiences");
        ValidateCollection(resume.Educations, "Resume.Educations");
        ValidateCollection(resume.Skills, "Resume.Skills");
        ValidateCollection(resume.Projects, "Resume.Projects");
        ValidateCollection(resume.Certifications, "Resume.Certifications");
        ValidateCollection(resume.Languages, "Resume.Languages");

        foreach (var experience in resume.Experiences.OrEmpty())
        {
            ValidateCollection(experience.Bullets, "Experience.Bullets");
        }

        foreach (var resumeSkill in resume.Skills.OrEmpty())
        {
            ArgumentNullException.ThrowIfNull(resumeSkill.Skill);
        }

        foreach (var project in resume.Projects.OrEmpty())
        {
            ValidateCollection(project.Skills, "Project.Skills");
            foreach (var projectSkill in project.Skills.OrEmpty())
            {
                ArgumentNullException.ThrowIfNull(projectSkill.Skill);
            }
        }
    }

    private static void ValidateCollection<T>(
        ImmutableArray<T> collection,
        string collectionName)
        where T : class
    {
        if (!collection.IsDefault && collection.Any(item => item is null))
        {
            throw new ArgumentException(
                $"Collection '{collectionName}' cannot contain null values.");
        }
    }

    private sealed record IssueText(string Title, string Description);
}

internal static class ImmutableArrayValidationExtensions
{
    public static IEnumerable<T> OrEmpty<T>(this ImmutableArray<T> source)
    {
        return source.IsDefault ? [] : source;
    }
}
