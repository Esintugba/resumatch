using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class KeywordMatchConfiguration : IEntityTypeConfiguration<KeywordMatch>
{
    public void Configure(EntityTypeBuilder<KeywordMatch> builder)
    {
        builder.ToTable("KeywordMatches");

        builder.HasKey(keywordMatch => keywordMatch.Id);

        builder.Property(keywordMatch => keywordMatch.Id)
            .ValueGeneratedNever();

        builder.Property(keywordMatch => keywordMatch.ResumeAnalysisId)
            .IsRequired();

        builder.Property(keywordMatch => keywordMatch.SkillId)
            .IsRequired(false);

        builder.Property(keywordMatch => keywordMatch.Keyword)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(keywordMatch => keywordMatch.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(keywordMatch => keywordMatch.Weight)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(keywordMatch => keywordMatch.ResumeSection)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(keywordMatch => keywordMatch.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(keywordMatch => keywordMatch.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(keywordMatch => keywordMatch.ResumeAnalysisId);

        builder.HasIndex(keywordMatch => keywordMatch.SkillId);

        builder.HasOne(keywordMatch => keywordMatch.ResumeAnalysis)
            .WithMany(analysis => analysis.KeywordMatches)
            .HasForeignKey(keywordMatch => keywordMatch.ResumeAnalysisId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(keywordMatch => keywordMatch.Skill)
            .WithMany()
            .HasForeignKey(keywordMatch => keywordMatch.SkillId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
