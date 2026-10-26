using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("JobApplications", table => table.HasCheckConstraint(
            "CK_JobApplications_ResumeOwnership",
            "(\"ResumeId\" IS NULL AND \"ResumeUserId\" IS NULL) OR " +
            "(\"ResumeId\" IS NOT NULL AND \"ResumeUserId\" IS NOT NULL AND " +
            "\"ResumeUserId\" = \"UserId\")"));

        builder.HasKey(application => application.Id);

        builder.Property(application => application.Id)
            .ValueGeneratedNever();

        builder.Property(application => application.Version)
            .IsRowVersion();

        builder.Property(application => application.UserId)
            .IsRequired();

        builder.Property(application => application.JobPostingId)
            .IsRequired();

        builder.Property(application => application.ResumeId)
            .IsRequired(false);

        builder.Property(application => application.ResumeUserId)
            .IsRequired(false);

        builder.Property(application => application.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(application => application.AppliedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(application => application.Notes)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(application => application.NextActionAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(application => application.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(application => application.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(application => application.UserId);

        builder.HasIndex(application => application.JobPostingId);

        builder.HasIndex(application => new { application.UserId, application.JobPostingId })
            .IsUnique();

        builder.HasIndex(application => application.ResumeId);

        builder.HasIndex(application => new { application.ResumeId, application.ResumeUserId });

        builder.HasIndex(application => application.Status);

        builder.HasIndex(application => application.NextActionAt);

        builder.HasOne(application => application.User)
            .WithMany(user => user.JobApplications)
            .HasForeignKey(application => application.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(application => application.JobPosting)
            .WithMany(jobPosting => jobPosting.Applications)
            .HasForeignKey(application => new { application.JobPostingId, application.UserId })
            .HasPrincipalKey(jobPosting => new { jobPosting.Id, jobPosting.UserId })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(application => application.Resume)
            .WithMany()
            .HasForeignKey(application => new { application.ResumeId, application.ResumeUserId })
            .HasPrincipalKey(resume => new { resume.Id, resume.UserId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
