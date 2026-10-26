using System.Collections.Immutable;
using Api.Application.Analysis.Engine.JobMatching;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Keywords;

public sealed class ExactKeywordMatcher : IKeywordMatcher
{
    private const string SkillsSection = "Skills";
    private const string ProjectsSection = "Projects";
    private const string ExperienceSection = "Experience";

    public ImmutableArray<KeywordMatchResult> Match(
        ResumeSnapshot resume,
        JobPostingSnapshot jobPosting)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(jobPosting);

        if (jobPosting.Skills.IsDefaultOrEmpty)
        {
            return ImmutableArray<KeywordMatchResult>.Empty;
        }

        var requirements = JobSkillRequirementSelector.Select(jobPosting);
        var weights = JobMatchWeightAllocator.Allocate(requirements);
        var evidence = ResumeEvidenceIndex.Create(resume);
        var results = ImmutableArray.CreateBuilder<KeywordMatchResult>(
            requirements.Length);

        foreach (var requirement in requirements)
        {
            var resumeSection = evidence.FindStrongestSection(
                requirement.CanonicalSkill);
            var status = resumeSection is null
                ? KeywordMatchStatus.Missing
                : KeywordMatchStatus.Matched;

            results.Add(new KeywordMatchResult(
                requirement.Skill.Skill.Id,
                requirement.CanonicalSkill.DisplayName,
                status,
                weights[requirement.CanonicalSkill.Key],
                resumeSection));
        }

        return results.MoveToImmutable();
    }

    private sealed class ResumeEvidenceIndex
    {
        private readonly HashSet<string> _explicitSkillKeys;
        private readonly HashSet<string> _projectSkillKeys;
        private readonly ImmutableArray<string> _projectTexts;
        private readonly ImmutableArray<string> _experienceTexts;

        private ResumeEvidenceIndex(
            HashSet<string> explicitSkillKeys,
            HashSet<string> projectSkillKeys,
            ImmutableArray<string> projectTexts,
            ImmutableArray<string> experienceTexts)
        {
            _explicitSkillKeys = explicitSkillKeys;
            _projectSkillKeys = projectSkillKeys;
            _projectTexts = projectTexts;
            _experienceTexts = experienceTexts;
        }

        public static ResumeEvidenceIndex Create(ResumeSnapshot resume)
        {
            var explicitSkillKeys = resume.Skills.IsDefaultOrEmpty
                ? new HashSet<string>(StringComparer.Ordinal)
                : resume.Skills
                    .Select(resumeSkill => CanonicalSkillDescriptor.Create(
                        resumeSkill.Skill).Key)
                    .ToHashSet(StringComparer.Ordinal);

            var projectSkillKeys = new HashSet<string>(StringComparer.Ordinal);
            var projectTexts = ImmutableArray.CreateBuilder<string>();

            if (!resume.Projects.IsDefaultOrEmpty)
            {
                foreach (var project in resume.Projects)
                {
                    AddNormalizedText(projectTexts, project.Name);
                    AddNormalizedText(projectTexts, project.Description);

                    if (project.Skills.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    foreach (var projectSkill in project.Skills)
                    {
                        projectSkillKeys.Add(
                            CanonicalSkillDescriptor.Create(projectSkill.Skill).Key);
                    }
                }
            }

            var experienceTexts = ImmutableArray.CreateBuilder<string>();
            if (!resume.Experiences.IsDefaultOrEmpty)
            {
                foreach (var experience in resume.Experiences)
                {
                    AddNormalizedText(experienceTexts, experience.Position);
                    AddNormalizedText(experienceTexts, experience.Description);

                    if (experience.Bullets.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    foreach (var bullet in experience.Bullets)
                    {
                        if ((!bullet.IsAiGenerated || bullet.IsUserApproved) &&
                            !string.IsNullOrWhiteSpace(bullet.Content))
                        {
                            AddNormalizedText(experienceTexts, bullet.Content);
                        }
                    }
                }
            }

            return new ResumeEvidenceIndex(
                explicitSkillKeys,
                projectSkillKeys,
                projectTexts.ToImmutable(),
                experienceTexts.ToImmutable());
        }

        public string? FindStrongestSection(CanonicalSkillDescriptor skill)
        {
            if (_explicitSkillKeys.Contains(skill.Key))
            {
                return SkillsSection;
            }

            if (_projectSkillKeys.Contains(skill.Key) ||
                ContainsPhrase(_projectTexts, skill.SearchPhrase))
            {
                return ProjectsSection;
            }

            return ContainsPhrase(_experienceTexts, skill.SearchPhrase)
                ? ExperienceSection
                : null;
        }

        private static bool ContainsPhrase(
            IEnumerable<string> normalizedTexts,
            string? searchPhrase)
        {
            return searchPhrase is not null &&
                   normalizedTexts.Any(text =>
                       TextNormalizer.ContainsExactPhrase(text, searchPhrase));
        }

        private static void AddNormalizedText(
            ICollection<string> target,
            string? value)
        {
            var normalized = TextNormalizer.Normalize(value);
            if (normalized.Length > 0)
            {
                target.Add(normalized);
            }
        }
    }
}
