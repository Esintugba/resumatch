using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ProjectSkillConfiguration : IEntityTypeConfiguration<ProjectSkill>
{
    public void Configure(EntityTypeBuilder<ProjectSkill> builder)
    {
        builder.ToTable("ProjectSkills");

        builder.HasKey(projectSkill => projectSkill.Id);

        builder.Property(projectSkill => projectSkill.Id)
            .ValueGeneratedNever();

        builder.Property(projectSkill => projectSkill.ResumeProjectId)
            .IsRequired();

        builder.Property(projectSkill => projectSkill.SkillId)
            .IsRequired();

        builder.Property(projectSkill => projectSkill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(projectSkill => projectSkill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(projectSkill => new { projectSkill.ResumeProjectId, projectSkill.SkillId })
            .IsUnique();

        builder.HasIndex(projectSkill => projectSkill.SkillId);

        builder.HasOne(projectSkill => projectSkill.Project)
            .WithMany(project => project.Skills)
            .HasForeignKey(projectSkill => projectSkill.ResumeProjectId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(projectSkill => projectSkill.Skill)
            .WithMany()
            .HasForeignKey(projectSkill => projectSkill.SkillId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
