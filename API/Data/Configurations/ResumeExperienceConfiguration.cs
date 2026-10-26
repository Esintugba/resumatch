using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeExperienceConfiguration : IEntityTypeConfiguration<ResumeExperience>
{
    public void Configure(EntityTypeBuilder<ResumeExperience> builder)
    {
        builder.ToTable("ResumeExperiences", table => table.HasCheckConstraint(
            "CK_ResumeExperiences_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(experience => experience.Id);

        builder.Property(experience => experience.Id)
            .ValueGeneratedNever();

        builder.Property(experience => experience.Version)
            .IsRowVersion();

        builder.Property(experience => experience.ResumeId)
            .IsRequired();

        builder.Property(experience => experience.CompanyName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(experience => experience.Position)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(experience => experience.Location)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(experience => experience.EmploymentType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(experience => experience.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(experience => experience.EndDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(experience => experience.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(experience => experience.Description)
            .IsRequired(false)
            .HasMaxLength(5000);

        builder.Property(experience => experience.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(experience => experience.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(experience => experience.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(experience => experience.ResumeId);

        builder.HasIndex(experience => new { experience.ResumeId, experience.SortOrder });

        builder.HasOne(experience => experience.Resume)
            .WithMany(resume => resume.Experiences)
            .HasForeignKey(experience => experience.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
