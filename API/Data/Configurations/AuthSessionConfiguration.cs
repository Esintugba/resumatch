using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("AuthSessions", table =>
        {
            table.HasCheckConstraint(
                "CK_AuthSessions_Lifetime",
                "\"IdleExpiresAt\" <= \"AbsoluteExpiresAt\"");
            table.HasCheckConstraint(
                "CK_AuthSessions_Revocation",
                "(\"RevokedAt\" IS NULL) = (\"RevokedReason\" IS NULL)");
        });

        builder.HasKey(session => session.Id);

        builder.Property(session => session.Id)
            .ValueGeneratedNever();

        builder.Property(session => session.LastUsedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.IdleExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.AbsoluteExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.RevokedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.RevokedReason)
            .HasConversion<short?>();

        builder.Property(session => session.DeviceLabel)
            .HasMaxLength(100);

        builder.Property(session => session.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(session => new { session.UserId, session.RevokedAt });
        builder.HasIndex(session => session.AbsoluteExpiresAt);

        builder.HasOne(session => session.User)
            .WithMany(user => user.Sessions)
            .HasForeignKey(session => session.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
