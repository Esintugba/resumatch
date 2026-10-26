using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeSkillConfiguration : IEntityTypeConfiguration<ResumeSkill>
{
    public void Configure(EntityTypeBuilder<ResumeSkill> builder)
    {
        builder.ToTable("ResumeSkills", table => table.HasCheckConstraint(
            "CK_ResumeSkills_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(resumeSkill => resumeSkill.Id);

        builder.Property(resumeSkill => resumeSkill.Id)
            .ValueGeneratedNever();

        builder.Property(resumeSkill => resumeSkill.ResumeId)
            .IsRequired();

        builder.Property(resumeSkill => resumeSkill.SkillId)
            .IsRequired();

        builder.Property(resumeSkill => resumeSkill.Level)
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(resumeSkill => resumeSkill.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(resumeSkill => resumeSkill.IsHighlighted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(resumeSkill => resumeSkill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(resumeSkill => resumeSkill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(resumeSkill => new { resumeSkill.ResumeId, resumeSkill.SkillId })
            .IsUnique();

        builder.HasIndex(resumeSkill => resumeSkill.SkillId);

        builder.HasOne(resumeSkill => resumeSkill.Resume)
            .WithMany(resume => resume.Skills)
            .HasForeignKey(resumeSkill => resumeSkill.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(resumeSkill => resumeSkill.Skill)
            .WithMany()
            .HasForeignKey(resumeSkill => resumeSkill.SkillId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
