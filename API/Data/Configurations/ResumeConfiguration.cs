using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("Resumes", table =>
        {
            table.HasCheckConstraint(
                "CK_Resumes_SourceNotSelf",
                "\"SourceResumeId\" IS NULL OR \"SourceResumeId\" <> \"Id\"");

            table.HasCheckConstraint(
                "CK_Resumes_SourceMustBeMaster",
                "(\"SourceResumeId\" IS NULL AND \"SourceResumeIsMaster\" IS NULL) OR " +
                "(\"SourceResumeId\" IS NOT NULL AND \"SourceResumeIsMaster\" IS TRUE AND " +
                "\"IsMasterResume\" IS FALSE)");
        });

        builder.HasKey(resume => resume.Id);

        builder.HasAlternateKey(resume => new { resume.Id, resume.UserId });

        builder.HasAlternateKey(resume => new
        {
            resume.Id,
            resume.UserId,
            resume.IsMasterResume
        });

        builder.Property(resume => resume.Id)
            .ValueGeneratedNever();

        builder.Property(resume => resume.Version)
            .IsRowVersion();

        builder.Property(resume => resume.UserId)
            .IsRequired();

        builder.Property(resume => resume.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(resume => resume.ProfessionalTitle)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(resume => resume.UnlistedSkillsNote)
            .HasMaxLength(500);

        builder.Property(resume => resume.Summary)
            .IsRequired(false)
            .HasMaxLength(3000);

        builder.Property(resume => resume.TemplateKey)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(resume => resume.TargetRole)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(resume => resume.IsMasterResume)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(resume => resume.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(resume => resume.SourceResumeId)
            .IsRequired(false);

        builder.Property(resume => resume.SourceResumeIsMaster)
            .IsRequired(false);

        builder.Property(resume => resume.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(resume => resume.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(resume => resume.LastExportedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(resume => resume.UserId);

        builder.HasIndex(resume => resume.SourceResumeId);

        builder.HasIndex(resume => new
        {
            resume.SourceResumeId,
            resume.UserId,
            resume.SourceResumeIsMaster
        });

        builder.HasOne(resume => resume.User)
            .WithMany(user => user.Resumes)
            .HasForeignKey(resume => resume.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(resume => resume.SourceResume)
            .WithMany(resume => resume.DerivedResumes)
            .HasForeignKey(resume => new
            {
                resume.SourceResumeId,
                resume.UserId,
                resume.SourceResumeIsMaster
            })
            .HasPrincipalKey(resume => new
            {
                resume.Id,
                resume.UserId,
                resume.IsMasterResume
            })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
