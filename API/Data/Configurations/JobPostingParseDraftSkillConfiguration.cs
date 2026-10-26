using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobPostingParseDraftSkillConfiguration :
    IEntityTypeConfiguration<JobPostingParseDraftSkill>
{
    public void Configure(EntityTypeBuilder<JobPostingParseDraftSkill> builder)
    {
        builder.ToTable("JobPostingParseDraftSkills", table =>
        {
            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkills_CanonicalKey_NotBlank",
                "\"CanonicalKey\" ~ '[^[:space:]]'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkills_ProposedImportanceScore",
                "\"ProposedImportanceScore\" >= 0 AND " +
                "\"ProposedImportanceScore\" <= 100");

            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkills_Confidence",
                "\"Confidence\" IS NULL OR " +
                "(\"Confidence\" >= 0 AND \"Confidence\" <= 1)");

            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkills_SortOrder",
                "\"SortOrder\" >= 0");

            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkills_Resolution",
                $"(\"ResolutionStatus\" IN ({(int)SkillResolutionStatus.Exact}, " +
                $"{(int)SkillResolutionStatus.Alias}) AND " +
                "\"ResolvedSkillId\" IS NOT NULL) OR " +
                $"(\"ResolutionStatus\" IN ({(int)SkillResolutionStatus.Ambiguous}, " +
                $"{(int)SkillResolutionStatus.Unresolved}) AND " +
                "\"ResolvedSkillId\" IS NULL)");
        });

        builder.HasKey(draftSkill => draftSkill.Id);

        builder.HasAlternateKey(draftSkill => new
        {
            draftSkill.Id,
            draftSkill.JobPostingParseDraftId
        });

        builder.Property(draftSkill => draftSkill.Id)
            .ValueGeneratedNever();

        builder.Property(draftSkill => draftSkill.JobPostingParseDraftId)
            .IsRequired();

        builder.Property(draftSkill => draftSkill.CandidateName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(draftSkill => draftSkill.CanonicalKey)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(draftSkill => draftSkill.ResolvedSkillId)
            .IsRequired(false);

        builder.Property(draftSkill => draftSkill.RequirementType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(draftSkill => draftSkill.ProposedImportanceScore)
            .IsRequired();

        builder.Property(draftSkill => draftSkill.Confidence)
            .HasPrecision(5, 4)
            .IsRequired(false);

        builder.Property(draftSkill => draftSkill.SortOrder)
            .IsRequired();

        builder.Property(draftSkill => draftSkill.ResolutionStatus)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(draftSkill => draftSkill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(draftSkill => draftSkill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(draftSkill => new
            {
                draftSkill.JobPostingParseDraftId,
                draftSkill.SortOrder
            })
            .IsUnique();

        builder.HasIndex(draftSkill => new
            {
                draftSkill.JobPostingParseDraftId,
                draftSkill.CanonicalKey
            })
            .IsUnique();

        builder.HasIndex(draftSkill => draftSkill.ResolvedSkillId);

        // PostgreSQL uses this unique index as the principal key for the
        // provenance FK that also verifies the resolved SkillId. EF Core keys
        // cannot contain the nullable ResolvedSkillId property, so the final
        // three-column FK is added explicitly by the migration.
        builder.HasIndex(draftSkill => new
            {
                draftSkill.Id,
                draftSkill.JobPostingParseDraftId,
                draftSkill.ResolvedSkillId
            })
            .IsUnique()
            .HasDatabaseName("UX_JobPostingParseDraftSkills_ProvenanceIdentity");

        builder.HasOne(draftSkill => draftSkill.JobPostingParseDraft)
            .WithMany(draft => draft.Skills)
            .HasForeignKey(draftSkill => draftSkill.JobPostingParseDraftId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(draftSkill => draftSkill.ResolvedSkill)
            .WithMany()
            .HasForeignKey(draftSkill => draftSkill.ResolvedSkillId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
