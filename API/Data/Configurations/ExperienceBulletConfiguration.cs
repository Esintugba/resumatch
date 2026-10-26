using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ExperienceBulletConfiguration : IEntityTypeConfiguration<ExperienceBullet>
{
    public void Configure(EntityTypeBuilder<ExperienceBullet> builder)
    {
        builder.ToTable("ExperienceBullets", table => table.HasCheckConstraint(
            "CK_ExperienceBullets_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(bullet => bullet.Id);

        builder.Property(bullet => bullet.Id)
            .ValueGeneratedNever();

        builder.Property(bullet => bullet.ResumeExperienceId)
            .IsRequired();

        builder.HasIndex(bullet => bullet.ResumeExperienceId);

        builder.Property(bullet => bullet.Content)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(bullet => bullet.OriginalContent)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(bullet => bullet.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(bullet => bullet.IsAiGenerated)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(bullet => bullet.IsUserApproved)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(bullet => bullet.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(bullet => bullet.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(bullet => bullet.ResumeExperience)
            .WithMany(experience => experience.Bullets)
            .HasForeignKey(bullet => bullet.ResumeExperienceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
