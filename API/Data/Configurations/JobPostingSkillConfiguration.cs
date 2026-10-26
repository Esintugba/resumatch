using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobPostingSkillConfiguration : IEntityTypeConfiguration<JobPostingSkill>
{
    public void Configure(EntityTypeBuilder<JobPostingSkill> builder)
    {
        builder.ToTable("JobPostingSkills", table =>
        {
            table.HasCheckConstraint(
                "CK_JobPostingSkills_ImportanceScore",
                "\"ImportanceScore\" >= 0 AND \"ImportanceScore\" <= 100");

            table.HasCheckConstraint(
                "CK_JobPostingSkills_ProvenanceStatus",
                "\"ProvenanceStatus\" IN (0, 1, 2, 3, 4)");

            table.HasCheckConstraint(
                "CK_JobPostingSkills_ProvenanceInvariant",
                "(\"ProvenanceStatus\" = 0 AND " +
                "\"SourceParseDraftId\" IS NULL AND " +
                "\"SourceParseDraftSkillId\" IS NULL AND " +
                "\"InvalidatedAt\" IS NULL AND NOT \"IsAiDetected\") OR " +
                "(\"ProvenanceStatus\" = 1 AND " +
                "\"SourceParseDraftId\" IS NOT NULL AND " +
                "\"SourceParseDraftSkillId\" IS NOT NULL AND " +
                "\"InvalidatedAt\" IS NULL AND \"IsAiDetected\") OR " +
                "(\"ProvenanceStatus\" = 2 AND " +
                "\"SourceParseDraftId\" IS NOT NULL AND " +
                "\"SourceParseDraftSkillId\" IS NOT NULL AND " +
                "\"InvalidatedAt\" IS NOT NULL AND \"IsAiDetected\") OR " +
                "(\"ProvenanceStatus\" = 3 AND " +
                "\"SourceParseDraftId\" IS NOT NULL AND " +
                "\"SourceParseDraftSkillId\" IS NOT NULL AND " +
                "\"InvalidatedAt\" IS NULL AND \"IsAiDetected\") OR " +
                "(\"ProvenanceStatus\" = 4 AND " +
                "\"SourceParseDraftId\" IS NULL AND " +
                "\"SourceParseDraftSkillId\" IS NULL AND " +
                "\"InvalidatedAt\" IS NULL AND \"IsAiDetected\")");
        });

        builder.HasKey(jobPostingSkill => jobPostingSkill.Id);

        builder.Property(jobPostingSkill => jobPostingSkill.Id)
            .ValueGeneratedNever();

        builder.Property(jobPostingSkill => jobPostingSkill.JobPostingId)
            .IsRequired();

        builder.Property(jobPostingSkill => jobPostingSkill.SkillId)
            .IsRequired();

        builder.Property(jobPostingSkill => jobPostingSkill.RequirementType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(jobPostingSkill => jobPostingSkill.ImportanceScore)
            .IsRequired();

        builder.Property(jobPostingSkill => jobPostingSkill.IsAiDetected)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(jobPostingSkill => jobPostingSkill.ProvenanceStatus)
            .HasConversion<int>()
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(jobPostingSkill => jobPostingSkill.SourceParseDraftId)
            .IsRequired(false);

        builder.Property(jobPostingSkill => jobPostingSkill.SourceParseDraftSkillId)
            .IsRequired(false);

        builder.Property(jobPostingSkill => jobPostingSkill.InvalidatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(jobPostingSkill => jobPostingSkill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(jobPostingSkill => jobPostingSkill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(jobPostingSkill => new { jobPostingSkill.JobPostingId, jobPostingSkill.SkillId })
            .IsUnique();

        builder.HasIndex(jobPostingSkill => jobPostingSkill.SkillId);

        builder.HasIndex(jobPostingSkill => jobPostingSkill.SourceParseDraftSkillId)
            .IsUnique()
            .HasFilter("\"SourceParseDraftSkillId\" IS NOT NULL");

        builder.HasIndex(jobPostingSkill => new
        {
            jobPostingSkill.JobPostingId,
            jobPostingSkill.ProvenanceStatus
        });

        builder.HasOne(jobPostingSkill => jobPostingSkill.JobPosting)
            .WithMany(jobPosting => jobPosting.Skills)
            .HasForeignKey(jobPostingSkill => jobPostingSkill.JobPostingId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(jobPostingSkill => jobPostingSkill.Skill)
            .WithMany()
            .HasForeignKey(jobPostingSkill => jobPostingSkill.SkillId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(jobPostingSkill => jobPostingSkill.SourceParseDraft)
            .WithMany()
            .HasForeignKey(jobPostingSkill => new
            {
                jobPostingSkill.SourceParseDraftId,
                jobPostingSkill.JobPostingId
            })
            .HasPrincipalKey(draft => new
            {
                draft.Id,
                draft.JobPostingId
            })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(jobPostingSkill => jobPostingSkill.SourceParseDraftSkill)
            .WithMany()
            .HasForeignKey(jobPostingSkill => new
            {
                jobPostingSkill.SourceParseDraftSkillId,
                jobPostingSkill.SourceParseDraftId
            })
            .HasPrincipalKey(draftSkill => new
            {
                draftSkill.Id,
                draftSkill.JobPostingParseDraftId
            })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
