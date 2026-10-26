using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeAnalysisConfiguration : IEntityTypeConfiguration<ResumeAnalysis>
{
    public void Configure(EntityTypeBuilder<ResumeAnalysis> builder)
    {
        builder.ToTable("ResumeAnalyses", table =>
        {
            table.HasCheckConstraint(
                "CK_ResumeAnalyses_AtsScore",
                "\"AtsScore\" >= 0 AND \"AtsScore\" <= 100");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_JobMatchScore",
                "\"JobMatchScore\" IS NULL OR (\"JobMatchScore\" >= 0 AND \"JobMatchScore\" <= 100)");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_StructureScore",
                "\"StructureScore\" >= 0 AND \"StructureScore\" <= 100");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_FormattingScore",
                "\"FormattingScore\" >= 0 AND \"FormattingScore\" <= 100");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_ContentScore",
                "\"ContentScore\" >= 0 AND \"ContentScore\" <= 100");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_KeywordScore",
                "\"KeywordScore\" >= 0 AND \"KeywordScore\" <= 100");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_JobPostingOwnership",
                "(\"JobPostingId\" IS NULL AND \"JobPostingUserId\" IS NULL) OR " +
                "(\"JobPostingId\" IS NOT NULL AND \"JobPostingUserId\" IS NOT NULL AND " +
                "\"JobPostingUserId\" = \"UserId\")");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_AnalyzerVersion_NotBlank",
                "\"AnalyzerVersion\" ~ '[^[:space:]]'");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_SourceJobPostingVersion",
                "\"SourceJobPostingVersion\" IS NULL OR " +
                "\"SourceJobPostingVersion\" > 0");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_SourceJobPostingInputHash",
                "\"SourceJobPostingInputHash\" IS NULL OR " +
                "\"SourceJobPostingInputHash\" ~ '^[0-9a-f]{64}$'");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_SourceJobPostingSkillSetHash",
                "\"SourceJobPostingSkillSetHash\" IS NULL OR " +
                "\"SourceJobPostingSkillSetHash\" ~ '^[0-9a-f]{64}$'");

            table.HasCheckConstraint(
                "CK_ResumeAnalyses_SourceProvenanceCompleteness",
                "(\"SourceJobPostingVersion\" IS NULL AND " +
                "\"SourceJobPostingInputHash\" IS NULL AND " +
                "\"SourceJobPostingSkillSetHash\" IS NULL) OR " +
                "(\"SourceJobPostingVersion\" IS NOT NULL AND " +
                "\"SourceJobPostingInputHash\" IS NOT NULL AND " +
                "\"SourceJobPostingSkillSetHash\" IS NOT NULL)");
        });

        builder.HasKey(analysis => analysis.Id);

        builder.Property(analysis => analysis.Id)
            .ValueGeneratedNever();

        builder.Property(analysis => analysis.UserId)
            .IsRequired();

        builder.Property(analysis => analysis.ResumeId)
            .IsRequired();

        builder.Property(analysis => analysis.JobPostingId)
            .IsRequired(false);

        builder.Property(analysis => analysis.JobPostingUserId)
            .IsRequired(false);

        builder.Property(analysis => analysis.SourceJobPostingVersion)
            .IsRequired(false);

        builder.Property(analysis => analysis.SourceJobPostingInputHash)
            .IsRequired(false)
            .HasMaxLength(64);

        builder.Property(analysis => analysis.SourceJobPostingSkillSetHash)
            .IsRequired(false)
            .HasMaxLength(64);

        builder.Property(analysis => analysis.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(analysis => analysis.AtsScore)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(analysis => analysis.JobMatchScore)
            .HasPrecision(5, 2)
            .IsRequired(false);

        builder.Property(analysis => analysis.StructureScore)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(analysis => analysis.FormattingScore)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(analysis => analysis.ContentScore)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(analysis => analysis.KeywordScore)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(analysis => analysis.Summary)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(analysis => analysis.AnalyzedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(analysis => analysis.AnalyzerVersion)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(analysis => analysis.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(analysis => analysis.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(analysis => analysis.ResumeId);

        builder.HasIndex(analysis => new { analysis.ResumeId, analysis.UserId });

        builder.HasIndex(analysis => analysis.JobPostingId);

        builder.HasIndex(analysis => new { analysis.JobPostingId, analysis.JobPostingUserId });

        builder.HasIndex(analysis => analysis.AnalyzedAt);

        builder.HasOne(analysis => analysis.Resume)
            .WithMany(resume => resume.Analyses)
            .HasForeignKey(analysis => new { analysis.ResumeId, analysis.UserId })
            .HasPrincipalKey(resume => new { resume.Id, resume.UserId })
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(analysis => analysis.JobPosting)
            .WithMany(jobPosting => jobPosting.ResumeAnalyses)
            .HasForeignKey(analysis => new { analysis.JobPostingId, analysis.JobPostingUserId })
            .HasPrincipalKey(jobPosting => new { jobPosting.Id, jobPosting.UserId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
