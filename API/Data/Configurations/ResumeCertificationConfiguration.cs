using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeCertificationConfiguration : IEntityTypeConfiguration<ResumeCertification>
{
    public void Configure(EntityTypeBuilder<ResumeCertification> builder)
    {
        builder.ToTable("ResumeCertifications", table => table.HasCheckConstraint(
            "CK_ResumeCertifications_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(certification => certification.Id);

        builder.Property(certification => certification.Id)
            .ValueGeneratedNever();

        builder.Property(certification => certification.ResumeId)
            .IsRequired();

        builder.HasIndex(certification => certification.ResumeId);

        builder.Property(certification => certification.Name)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(certification => certification.IssuingOrganization)
            .IsRequired(false)
            .HasMaxLength(250);

        builder.Property(certification => certification.IssueDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(certification => certification.ExpirationDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(certification => certification.CredentialId)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(certification => certification.CredentialUrl)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.Property(certification => certification.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(certification => certification.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(certification => certification.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(certification => certification.Resume)
            .WithMany(resume => resume.Certifications)
            .HasForeignKey(certification => certification.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
