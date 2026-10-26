using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobPostingParseDraftConfiguration :
    IEntityTypeConfiguration<JobPostingParseDraft>
{
    public void Configure(EntityTypeBuilder<JobPostingParseDraft> builder)
    {
        builder.ToTable("JobPostingParseDrafts", table =>
        {
            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_SourceDescriptionHash",
                "\"SourceDescriptionHash\" ~ '^[0-9a-f]{64}$'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_SourceTitle_NotBlank",
                "\"SourceTitle\" ~ '[^[:space:]]'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_SourceInputHash",
                "\"SourceInputHash\" ~ '^[0-9a-f]{64}$'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_SkillCatalogHash",
                "\"SkillCatalogHash\" IS NULL OR " +
                "\"SkillCatalogHash\" ~ '^[0-9a-f]{64}$'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_ParserVersion_NotBlank",
                "\"ParserVersion\" ~ '[^[:space:]]'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_SourceJobPostingVersion",
                "\"SourceJobPostingVersion\" > 0");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_AttemptCount",
                "\"AttemptCount\" >= 0");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_Status",
                "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_LeaseOwner_NotBlank",
                "\"LeaseOwner\" IS NULL OR " +
                "\"LeaseOwner\" ~ '[^[:space:]]'");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_ProcessingLease",
                "(\"Status\" = 1 AND " +
                "\"LeaseOwner\" IS NOT NULL AND " +
                "\"LeaseExpiresAt\" IS NOT NULL AND " +
                "\"LastAttemptAt\" IS NOT NULL AND " +
                "\"NextAttemptAt\" IS NULL AND " +
                "\"LeaseExpiresAt\" > \"LastAttemptAt\") OR " +
                "(\"Status\" <> 1 AND " +
                "\"LeaseOwner\" IS NULL AND " +
                "\"LeaseExpiresAt\" IS NULL)");

            table.HasCheckConstraint(
                "CK_JobPostingParseDrafts_NextAttemptStatus",
                "\"NextAttemptAt\" IS NULL OR \"Status\" = 0");
        });

        builder.HasKey(draft => draft.Id);

        builder.HasAlternateKey(draft => new
        {
            draft.Id,
            draft.JobPostingId
        });

        builder.Property(draft => draft.Id)
            .ValueGeneratedNever();

        builder.Property(draft => draft.Version)
            .IsRowVersion();

        builder.Property(draft => draft.JobPostingId)
            .IsRequired();

        builder.Property(draft => draft.UserId)
            .IsRequired();

        builder.Property(draft => draft.SourceJobPostingVersion)
            .IsRequired();

        builder.Property(draft => draft.SourceTitle)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(draft => draft.SourceDescriptionHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(draft => draft.SourceInputHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(draft => draft.SourceDescription)
            .IsRequired()
            .HasMaxLength(50_000);

        builder.Property(draft => draft.SkillCatalogHash)
            .IsRequired(false)
            .HasMaxLength(64);

        builder.Property(draft => draft.ParserVersion)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(draft => draft.ProviderRequestId)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(draft => draft.ProviderName)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(draft => draft.ProviderModel)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(draft => draft.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(draft => draft.FailureCode)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(draft => draft.AttemptCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(draft => draft.LeaseOwner)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(draft => draft.RequestedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(draft => draft.LeaseExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.LastAttemptAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.NextAttemptAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.ConfirmedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.ExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(draft => draft.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(draft => draft.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(draft => new { draft.JobPostingId, draft.UserId });

        builder.HasIndex(draft => new
        {
            draft.JobPostingId,
            draft.SourceDescriptionHash,
            draft.ParserVersion
        });

        builder.HasIndex(draft => new
            {
                draft.JobPostingId,
                draft.SourceInputHash,
                draft.ParserVersion
            })
            .HasDatabaseName("UX_JobPostingParseDrafts_ActiveFingerprint")
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2)");

        builder.HasIndex(draft => new
            {
                draft.Status,
                draft.NextAttemptAt,
                draft.RequestedAt
            })
            .HasDatabaseName("IX_JobPostingParseDrafts_PendingClaim")
            .HasFilter("\"Status\" = 0");

        builder.HasIndex(draft => new
            {
                draft.Status,
                draft.LeaseExpiresAt
            })
            .HasDatabaseName("IX_JobPostingParseDrafts_ProcessingLease")
            .HasFilter("\"Status\" = 1");

        builder.HasOne(draft => draft.JobPosting)
            .WithMany()
            .HasForeignKey(draft => new { draft.JobPostingId, draft.UserId })
            .HasPrincipalKey(jobPosting => new
            {
                jobPosting.Id,
                jobPosting.UserId
            })
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
