using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class AnalysisIssueConfiguration : IEntityTypeConfiguration<AnalysisIssue>
{
    public void Configure(EntityTypeBuilder<AnalysisIssue> builder)
    {
        builder.ToTable("AnalysisIssues");

        builder.HasKey(issue => issue.Id);

        builder.Property(issue => issue.Id)
            .ValueGeneratedNever();

        builder.Property(issue => issue.ResumeAnalysisId)
            .IsRequired();

        builder.Property(issue => issue.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(issue => issue.Severity)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(issue => issue.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(issue => issue.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(issue => issue.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(issue => issue.ResumeSection)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(issue => issue.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(issue => issue.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(issue => issue.ResumeAnalysisId);

        builder.HasOne(issue => issue.ResumeAnalysis)
            .WithMany(analysis => analysis.Issues)
            .HasForeignKey(issue => issue.ResumeAnalysisId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
