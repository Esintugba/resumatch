using System.Globalization;
using Api.Application.Analysis.Queries;
using Api.Entities.Enums;

namespace API.Tests.Analysis;

public sealed class JobPostingSkillSnapshotHashCalculatorTests
{
    [Fact]
    public void Same_logical_input_produces_deterministic_lowercase_sha256()
    {
        var skills = CreateSkills();

        var first = JobPostingSkillSnapshotHashCalculator.Compute(skills);
        var second = JobPostingSkillSnapshotHashCalculator.Compute(skills);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Collection_order_does_not_change_hash()
    {
        var skills = CreateSkills();

        Assert.Equal(
            JobPostingSkillSnapshotHashCalculator.Compute(skills),
            JobPostingSkillSnapshotHashCalculator.Compute(skills.Reverse()));
    }

    [Fact]
    public void Skill_identifier_changes_hash()
    {
        var original = CreateSkills();
        var changed = original.ToArray();
        changed[0] = changed[0] with { SkillId = Guid.NewGuid() };

        Assert.NotEqual(
            JobPostingSkillSnapshotHashCalculator.Compute(original),
            JobPostingSkillSnapshotHashCalculator.Compute(changed));
    }

    [Fact]
    public void Requirement_type_changes_hash()
    {
        var original = CreateSkills();
        var changed = original.ToArray();
        changed[0] = changed[0] with
        {
            RequirementType = JobSkillRequirementType.Mentioned
        };

        Assert.NotEqual(
            JobPostingSkillSnapshotHashCalculator.Compute(original),
            JobPostingSkillSnapshotHashCalculator.Compute(changed));
    }

    [Fact]
    public void Importance_score_changes_hash()
    {
        var original = CreateSkills();
        var changed = original.ToArray();
        changed[0] = changed[0] with { ImportanceScore = 89 };

        Assert.NotEqual(
            JobPostingSkillSnapshotHashCalculator.Compute(original),
            JobPostingSkillSnapshotHashCalculator.Compute(changed));
    }

    [Fact]
    public void Canonical_skill_name_changes_hash()
    {
        var original = CreateSkills();
        var changed = original.ToArray();
        changed[0] = changed[0] with
        {
            Name = "PostgreSQL",
            NormalizedName = "postgresql"
        };

        Assert.NotEqual(
            JobPostingSkillSnapshotHashCalculator.Compute(original),
            JobPostingSkillSnapshotHashCalculator.Compute(changed));
    }

    [Fact]
    public void Empty_collection_has_deterministic_hash()
    {
        var empty = Array.Empty<JobPostingSkillFingerprintItem>();
        var first = JobPostingSkillSnapshotHashCalculator.Compute(empty);
        var second = JobPostingSkillSnapshotHashCalculator.Compute(empty);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Duplicate_skill_identifier_is_rejected()
    {
        var duplicateId = Guid.NewGuid();
        JobPostingSkillFingerprintItem[] skills =
        [
            new(duplicateId, JobSkillRequirementType.Required, 90),
            new(duplicateId, JobSkillRequirementType.Preferred, 50)
        ];

        Assert.Throws<ArgumentException>(() =>
            JobPostingSkillSnapshotHashCalculator.Compute(skills));
    }

    [Fact]
    public void Hash_is_culture_independent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var turkish = JobPostingSkillSnapshotHashCalculator.Compute(
                CreateSkills());

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var english = JobPostingSkillSnapshotHashCalculator.Compute(
                CreateSkills());

            Assert.Equal(turkish, english);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static JobPostingSkillFingerprintItem[] CreateSkills() =>
    [
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            JobSkillRequirementType.Preferred,
            60),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            JobSkillRequirementType.Required,
            90)
    ];
}
