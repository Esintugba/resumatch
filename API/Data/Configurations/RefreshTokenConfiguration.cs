using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", table =>
            table.HasCheckConstraint(
                "CK_RefreshTokens_TokenHashLength",
                "octet_length(\"TokenHash\") = 32"));

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .IsRequired();

        builder.Property(token => token.ConsumedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(token => token.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(token => token.TokenHash)
            .IsUnique();
        builder.HasIndex(token => token.SessionId);

        builder.HasOne(token => token.Session)
            .WithMany(session => session.RefreshTokens)
            .HasForeignKey(token => token.SessionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
