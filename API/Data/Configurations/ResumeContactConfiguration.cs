using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class ResumeContactConfiguration : IEntityTypeConfiguration<ResumeContact>
{
    public void Configure(EntityTypeBuilder<ResumeContact> builder)
    {
        builder.ToTable("ResumeContacts");

        builder.HasKey(contact => contact.Id);

        builder.Property(contact => contact.Id)
            .ValueGeneratedNever();

        builder.Property(contact => contact.ResumeId)
            .IsRequired();

        builder.HasIndex(contact => contact.ResumeId)
            .IsUnique();

        builder.Property(contact => contact.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(contact => contact.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(contact => contact.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(contact => contact.PhoneNumber)
            .IsRequired(false)
            .HasMaxLength(30);

        builder.Property(contact => contact.City)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(contact => contact.Country)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(contact => contact.PostalCode)
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(contact => contact.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(contact => contact.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(contact => contact.Resume)
            .WithOne(resume => resume.Contact)
            .HasForeignKey<ResumeContact>(contact => contact.ResumeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
