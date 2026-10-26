using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("JobPostings");

        builder.HasKey(jobPosting => jobPosting.Id);

        builder.HasAlternateKey(jobPosting => new { jobPosting.Id, jobPosting.UserId });

        builder.Property(jobPosting => jobPosting.Id)
            .ValueGeneratedNever();

        builder.Property(jobPosting => jobPosting.Version)
            .IsRowVersion();

        builder.Property(jobPosting => jobPosting.UserId)
            .IsRequired();

        builder.Property(jobPosting => jobPosting.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(jobPosting => jobPosting.CompanyName)
            .IsRequired(false)
            .HasMaxLength(250);

        builder.Property(jobPosting => jobPosting.Location)
            .IsRequired(false)
            .HasMaxLength(250);

        builder.Property(jobPosting => jobPosting.WorkType)
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(jobPosting => jobPosting.EmploymentType)
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(jobPosting => jobPosting.Description)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(jobPosting => jobPosting.SourceUrl)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(jobPosting => jobPosting.PublishedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(jobPosting => jobPosting.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(jobPosting => jobPosting.ParsedSummary)
            .IsRequired(false)
            .HasMaxLength(5000);

        builder.Property(jobPosting => jobPosting.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(jobPosting => jobPosting.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(jobPosting => jobPosting.UserId);

        builder.HasIndex(jobPosting => jobPosting.Status);

        builder.HasOne(jobPosting => jobPosting.User)
            .WithMany(user => user.JobPostings)
            .HasForeignKey(jobPosting => jobPosting.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
