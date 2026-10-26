using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiProviderCircuitProbeLeaseConfiguration :
    IEntityTypeConfiguration<AiProviderCircuitProbeLease>
{
    public void Configure(EntityTypeBuilder<AiProviderCircuitProbeLease> builder)
    {
        builder.ToTable("AiProviderCircuitProbeLeases", table =>
        {
            table.HasCheckConstraint("CK_AiProviderCircuitProbeLeases_Status",
                "\"Status\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_AiProviderCircuitProbeLeases_Identity",
                "length(btrim(\"ProviderScope\")) > 0 AND " +
                "\"Generation\" >= 0 AND \"Sequence\" > 0 AND " +
                "\"OwnerToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_AiProviderCircuitProbeLeases_Timestamps",
                "\"ExpiresAt\" > \"AcquiredAt\" AND " +
                "(\"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"AcquiredAt\")");
            table.HasCheckConstraint("CK_AiProviderCircuitProbeLeases_Lifecycle",
                "(\"Status\" = 0 AND \"CompletedAt\" IS NULL) OR " +
                "(\"Status\" IN (1, 2) AND \"CompletedAt\" IS NOT NULL)");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ProviderScope).IsRequired().HasMaxLength(100);
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.AcquiredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired(false);

        builder.HasIndex(item => item.OwnerToken).IsUnique();
        builder.HasIndex(item => new
        {
            item.ProviderScope,
            item.Generation,
            item.Sequence
        }).IsUnique();
        builder.HasIndex(item => new
        {
            item.ProviderScope,
            item.Generation,
            item.Status,
            item.ExpiresAt
        });
    }
}
