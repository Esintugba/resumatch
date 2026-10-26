using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class AnalysisRecommendationConfiguration : IEntityTypeConfiguration<AnalysisRecommendation>
{
    public void Configure(EntityTypeBuilder<AnalysisRecommendation> builder)
    {
        builder.ToTable("AnalysisRecommendations", table =>
        {
            table.HasCheckConstraint(
                "CK_AnalysisRecommendations_SortOrder",
                "\"SortOrder\" IS NULL OR \"SortOrder\" >= 0");
        });

        builder.HasKey(recommendation => recommendation.Id);

        builder.Property(recommendation => recommendation.Id)
            .ValueGeneratedNever();

        builder.Property(recommendation => recommendation.ResumeAnalysisId)
            .IsRequired();

        builder.Property(recommendation => recommendation.RuleCode)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(recommendation => recommendation.SortOrder)
            .IsRequired(false);

        builder.Property(recommendation => recommendation.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(recommendation => recommendation.Priority)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(recommendation => recommendation.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(recommendation => recommendation.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(recommendation => recommendation.TargetSection)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(recommendation => recommendation.OriginalContent)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(recommendation => recommendation.SuggestedContent)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(recommendation => recommendation.IsAiGenerated)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(recommendation => recommendation.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(recommendation => recommendation.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(recommendation => recommendation.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(recommendation => recommendation.ResumeAnalysisId);

        builder.HasIndex(recommendation => new
            {
                recommendation.ResumeAnalysisId,
                recommendation.SortOrder
            })
            .IsUnique()
            .HasFilter("\"SortOrder\" IS NOT NULL");

        builder.HasOne(recommendation => recommendation.ResumeAnalysis)
            .WithMany(analysis => analysis.Recommendations)
            .HasForeignKey(recommendation => recommendation.ResumeAnalysisId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
