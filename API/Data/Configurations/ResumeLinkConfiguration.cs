using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeLinkConfiguration : IEntityTypeConfiguration<ResumeLink>
{
    public void Configure(EntityTypeBuilder<ResumeLink> builder)
    {
        builder.ToTable("ResumeLinks");

        builder.HasKey(link => link.Id);

        builder.Property(link => link.Id)
            .ValueGeneratedNever();

        builder.Property(link => link.ResumeId)
            .IsRequired();

        builder.HasIndex(link => link.ResumeId);

        builder.Property(link => link.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(link => link.Label)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(link => link.Url)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(link => link.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(link => link.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(link => link.Resume)
            .WithMany(resume => resume.Links)
            .HasForeignKey(link => link.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
