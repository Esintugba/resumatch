using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("Skills");

        builder.HasKey(skill => skill.Id);

        builder.Property(skill => skill.Id)
            .ValueGeneratedNever();

        builder.Property(skill => skill.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(skill => skill.NormalizedName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(skill => skill.NameTr)
            .HasMaxLength(150);

        builder.Property(skill => skill.NormalizedNameTr)
            .HasMaxLength(150);

        builder.Property(skill => skill.Category)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(skill => skill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(skill => skill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(skill => skill.NormalizedName);

        builder.HasIndex(skill => new { skill.NormalizedName, skill.Category })
            .IsUnique();
    }
}
