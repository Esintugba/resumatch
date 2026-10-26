using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiProviderConcurrencyLeaseConfiguration :
    IEntityTypeConfiguration<AiProviderConcurrencyLease>
{
    public void Configure(EntityTypeBuilder<AiProviderConcurrencyLease> builder)
    {
        builder.ToTable("AiProviderConcurrencyLeases", table =>
        {
            table.HasCheckConstraint("CK_AiProviderConcurrencyLeases_Status",
                "\"Status\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_AiProviderConcurrencyLeases_Identity",
                "length(btrim(\"ProviderScope\")) > 0 AND " +
                "\"OwnerToken\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_AiProviderConcurrencyLeases_Time",
                "\"AcquiredAt\" = \"CreatedAt\" AND \"ExpiresAt\" > \"AcquiredAt\"");
            table.HasCheckConstraint("CK_AiProviderConcurrencyLeases_Lifecycle",
                "(\"Status\" = 0 AND \"ReleasedAt\" IS NULL) OR " +
                "(\"Status\" IN (1, 2) AND \"ReleasedAt\" IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ProviderScope).IsRequired().HasMaxLength(100);
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.AcquiredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.ReleasedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(item => new { item.ProviderScope, item.Status, item.ExpiresAt });
        builder.HasIndex(item => item.OwnerToken).IsUnique();
        builder.HasIndex(item => item.CorrelationId);
    }
}
