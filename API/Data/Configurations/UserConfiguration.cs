using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(user => user.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(user => user.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property<string>("NormalizedEmail")
            .IsRequired()
            .HasMaxLength(255)
            .HasComputedColumnSql("lower(btrim(\"Email\"))", stored: true);

        builder.HasIndex("NormalizedEmail")
            .IsUnique();

        builder.Property(user => user.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(user => user.ProfileImageUrl)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.Property(user => user.PhoneNumber)
            .IsRequired(false)
            .HasMaxLength(30);

        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(user => user.LastLoginAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(user => user.IsEmailVerified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(user => user.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasMany(user => user.Resumes)
            .WithOne(resume => resume.User)
            .HasForeignKey(resume => resume.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(user => user.JobPostings)
            .WithOne(jobPosting => jobPosting.User)
            .HasForeignKey(jobPosting => jobPosting.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(user => user.JobApplications)
            .WithOne(jobApplication => jobApplication.User)
            .HasForeignKey(jobApplication => jobApplication.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
