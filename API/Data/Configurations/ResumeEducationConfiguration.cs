using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeEducationConfiguration : IEntityTypeConfiguration<ResumeEducation>
{
    public void Configure(EntityTypeBuilder<ResumeEducation> builder)
    {
        builder.ToTable("ResumeEducations", table => table.HasCheckConstraint(
            "CK_ResumeEducations_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(education => education.Id);

        builder.Property(education => education.Id)
            .ValueGeneratedNever();

        builder.Property(education => education.ResumeId)
            .IsRequired();

        builder.HasIndex(education => education.ResumeId);

        builder.Property(education => education.InstitutionName)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(education => education.Degree)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(education => education.FieldOfStudy)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(education => education.StartDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(education => education.EndDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(education => education.Location)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(education => education.Grade)
            .IsRequired(false)
            .HasPrecision(5, 2);

        builder.Property(education => education.GradeScale)
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(education => education.Description)
            .IsRequired(false)
            .HasMaxLength(3000);

        builder.Property(education => education.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(education => education.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(education => education.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(education => education.Resume)
            .WithMany(resume => resume.Educations)
            .HasForeignKey(education => education.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
