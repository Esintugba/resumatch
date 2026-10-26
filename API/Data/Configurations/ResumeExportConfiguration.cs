using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeExportConfiguration : IEntityTypeConfiguration<ResumeExport>
{
    public void Configure(EntityTypeBuilder<ResumeExport> builder)
    {
        builder.ToTable("ResumeExports");

        builder.HasKey(resumeExport => resumeExport.Id);

        builder.Property(resumeExport => resumeExport.Id)
            .ValueGeneratedNever();

        builder.Property(resumeExport => resumeExport.ResumeId)
            .IsRequired();

        builder.Property(resumeExport => resumeExport.Format)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(resumeExport => resumeExport.TemplateKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(resumeExport => resumeExport.FileUrl)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(resumeExport => resumeExport.ExportedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(resumeExport => resumeExport.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(resumeExport => resumeExport.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(resumeExport => resumeExport.ResumeId);

        builder.HasOne(resumeExport => resumeExport.Resume)
            .WithMany(resume => resume.Exports)
            .HasForeignKey(resumeExport => resumeExport.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
