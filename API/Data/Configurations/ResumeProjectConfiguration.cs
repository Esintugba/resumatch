using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeProjectConfiguration : IEntityTypeConfiguration<ResumeProject>
{
    public void Configure(EntityTypeBuilder<ResumeProject> builder)
    {
        builder.ToTable("ResumeProjects", table => table.HasCheckConstraint(
            "CK_ResumeProjects_SortOrder",
            "\"SortOrder\" >= 0"));

        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id)
            .ValueGeneratedNever();

        builder.Property(project => project.Version)
            .IsRowVersion();

        builder.Property(project => project.ResumeId)
            .IsRequired();

        builder.Property(project => project.Name)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(project => project.Description)
            .IsRequired(false)
            .HasMaxLength(5000);

        builder.Property(project => project.Url)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.Property(project => project.RepositoryUrl)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.Property(project => project.StartDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(project => project.EndDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(project => project.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(project => project.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(project => project.ResumeId);

        builder.HasOne(project => project.Resume)
            .WithMany(resume => resume.Projects)
            .HasForeignKey(project => project.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
