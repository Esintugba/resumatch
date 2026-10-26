using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobPostingParseDraftSkillEvidenceConfiguration :
    IEntityTypeConfiguration<JobPostingParseDraftSkillEvidence>
{
    public void Configure(
        EntityTypeBuilder<JobPostingParseDraftSkillEvidence> builder)
    {
        builder.ToTable("JobPostingParseDraftSkillEvidences", table =>
        {
            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkillEvidences_Start",
                "\"Start\" >= 0");

            table.HasCheckConstraint(
                "CK_JobPostingParseDraftSkillEvidences_Length",
                "\"Length\" > 0");
        });

        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.Id)
            .ValueGeneratedNever();

        builder.Property(evidence => evidence.JobPostingParseDraftSkillId)
            .IsRequired();

        builder.Property(evidence => evidence.Text)
            .IsRequired()
            .HasMaxLength(1_000);

        builder.Property(evidence => evidence.Start)
            .IsRequired();

        builder.Property(evidence => evidence.Length)
            .IsRequired();

        builder.Property(evidence => evidence.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(evidence => evidence.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // A validated source range can only map to one exact source text.
        // Excluding Text avoids PostgreSQL B-tree row-size failures for
        // 1,000-character Unicode evidence while still rejecting duplicates.
        builder.HasIndex(evidence => new
            {
                evidence.JobPostingParseDraftSkillId,
                evidence.Start,
                evidence.Length
            })
            .IsUnique();

        builder.HasOne(evidence => evidence.JobPostingParseDraftSkill)
            .WithMany(draftSkill => draftSkill.Evidence)
            .HasForeignKey(evidence => evidence.JobPostingParseDraftSkillId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
