using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeLanguageConfiguration : IEntityTypeConfiguration<ResumeLanguage>
{
    public void Configure(EntityTypeBuilder<ResumeLanguage> builder)
    {
        builder.ToTable("ResumeLanguages", table => table.HasCheckConstraint(
            "CK_ResumeLanguages_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(language => language.Id);

        builder.Property(language => language.Id)
            .ValueGeneratedNever();

        builder.Property(language => language.ResumeId)
            .IsRequired();

        builder.Property(language => language.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property<string>("NormalizedName")
            .IsRequired()
            .HasMaxLength(100)
            .HasComputedColumnSql("lower(btrim(\"Name\"))", stored: true);

        builder.HasIndex("ResumeId", "NormalizedName")
            .IsUnique();

        builder.Property(language => language.Proficiency)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(language => language.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(language => language.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(language => language.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(language => language.Resume)
            .WithMany(resume => resume.Languages)
            .HasForeignKey(language => language.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
