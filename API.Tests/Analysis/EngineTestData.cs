using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

internal static class EngineTestData
{
    public static readonly DateTimeOffset AsOf =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static ResumeSnapshot Resume(
        string? summary = null,
        ResumeContactSnapshot? contact = null,
        IEnumerable<ResumeLinkSnapshot>? links = null,
        IEnumerable<ExperienceSnapshot>? experiences = null,
        IEnumerable<EducationSnapshot>? educations = null,
        IEnumerable<ResumeSkillSnapshot>? skills = null,
        IEnumerable<ProjectSnapshot>? projects = null,
        IEnumerable<CertificationSnapshot>? certifications = null,
        IEnumerable<LanguageSnapshot>? languages = null)
    {
        return new ResumeSnapshot(
            Id(1),
            "Software Engineer Resume",
            "Software Engineer",
            summary,
            true,
            false,
            null,
            "default",
            "Backend Engineer",
            null,
            AsOf.AddYears(-1),
            null,
            contact,
            ToImmutable(links),
            ToImmutable(experiences),
            ToImmutable(educations),
            ToImmutable(skills),
            ToImmutable(projects),
            ToImmutable(certifications),
            ToImmutable(languages));
    }

    public static ResumeContactSnapshot Contact(string email = "dev@example.com")
    {
        return new ResumeContactSnapshot(
            Id(2),
            "Ada",
            "Lovelace",
            email,
            "+90 555 555 55 55",
            "Istanbul",
            "Türkiye",
            null);
    }

    public static SkillSnapshot Skill(
        int id,
        string name,
        string? normalizedName = null,
        SkillCategory category = SkillCategory.Backend)
    {
        return new SkillSnapshot(
            Id(id),
            name,
            normalizedName ?? name.ToLowerInvariant(),
            category);
    }

    public static ResumeSkillSnapshot ResumeSkill(
        SkillSnapshot skill,
        int sortOrder = 0)
    {
        return new ResumeSkillSnapshot(
            Id(1_000 + sortOrder + GetStableSuffix(skill.Id)),
            skill,
            SkillLevel.Advanced,
            sortOrder,
            false);
    }

    public static ExperienceBulletSnapshot Bullet(
        int id,
        string content,
        int sortOrder = 0,
        bool isAiGenerated = false,
        bool isUserApproved = false)
    {
        return new ExperienceBulletSnapshot(
            Id(id),
            content,
            sortOrder,
            isAiGenerated,
            isUserApproved,
            null);
    }

    public static ExperienceSnapshot Experience(
        int id,
        string position = "Software Engineer",
        string? description = "Built reliable backend services.",
        int sortOrder = 0,
        IEnumerable<ExperienceBulletSnapshot>? bullets = null)
    {
        return new ExperienceSnapshot(
            Id(id),
            "Example Company",
            position,
            "Istanbul",
            EmploymentType.FullTime,
            new DateOnly(2022, 1, 1),
            new DateOnly(2024, 1, 1),
            false,
            description,
            sortOrder,
            ToImmutable(bullets));
    }

    public static ProjectSnapshot Project(
        int id,
        string name = "Platform",
        string? description = "Built a production platform.",
        int sortOrder = 0,
        IEnumerable<SkillSnapshot>? skills = null)
    {
        var projectSkills = (skills ?? [])
            .Select((skill, index) => new ProjectSkillSnapshot(
                Id(2_000 + id * 10 + index),
                skill));

        return new ProjectSnapshot(
            Id(id),
            name,
            description,
            null,
            null,
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 1),
            sortOrder,
            projectSkills.ToImmutableArray());
    }

    public static JobPostingSnapshot Job(
        params JobPostingSkillSnapshot[] skills)
    {
        return new JobPostingSnapshot(
            Id(500),
            "Backend Engineer",
            "Example Company",
            "Remote",
            WorkType.Remote,
            EmploymentType.FullTime,
            "Backend engineering role.",
            null,
            AsOf.AddDays(-1),
            JobPostingStatus.Analyzed,
            null,
            skills.ToImmutableArray());
    }

    public static JobPostingSkillSnapshot JobSkill(
        int id,
        SkillSnapshot skill,
        JobSkillRequirementType requirementType,
        int importance = 100)
    {
        return new JobPostingSkillSnapshot(
            Id(10_000 + id),
            skill,
            requirementType,
            importance,
            true);
    }

    public static AnalysisInput Input(
        ResumeSnapshot resume,
        JobPostingSnapshot? job = null)
    {
        return new AnalysisInput(resume, job, AsOf);
    }

    public static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static ImmutableArray<T> ToImmutable<T>(IEnumerable<T>? values)
    {
        return values?.ToImmutableArray() ?? ImmutableArray<T>.Empty;
    }

    private static int GetStableSuffix(Guid value)
    {
        return Math.Abs(BitConverter.ToInt32(value.ToByteArray(), 0) % 1000);
    }
}
