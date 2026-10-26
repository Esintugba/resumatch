using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiUsageReservationConfiguration :
    IEntityTypeConfiguration<AiUsageReservation>
{
    public void Configure(EntityTypeBuilder<AiUsageReservation> builder)
    {
        builder.ToTable("AiUsageReservations", table =>
        {
            table.HasCheckConstraint("CK_AiUsageReservations_Attempt",
                "\"AttemptCount\" > 0 AND \"ReservedRequestCount\" = 1 AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"DraftId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "length(btrim(\"AttemptKey\")) > 0 AND \"ExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint("CK_AiUsageReservations_ReservedTokens",
                "\"ReservedInputTokens\" >= 0 AND \"ReservedOutputTokens\" >= 0 AND " +
                "\"ReservedTotalTokens\" = \"ReservedInputTokens\" + \"ReservedOutputTokens\"");
            table.HasCheckConstraint("CK_AiUsageReservations_Status",
                "\"Status\" IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_AiUsageReservations_ActualUsage",
                "(\"ActualInputTokens\" IS NULL AND \"ActualOutputTokens\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR " +
                "(\"ActualInputTokens\" IS NOT NULL AND \"ActualOutputTokens\" IS NOT NULL AND " +
                "\"ActualTotalTokens\" IS NOT NULL AND \"ActualInputTokens\" >= 0 AND \"ActualOutputTokens\" >= 0 AND " +
                "\"ActualTotalTokens\" = \"ActualInputTokens\" + \"ActualOutputTokens\")");
            table.HasCheckConstraint("CK_AiUsageReservations_Overage",
                "\"OverageInputTokens\" >= 0 AND \"OverageOutputTokens\" >= 0 AND " +
                "\"OverageTotalTokens\" >= 0 AND " +
                "((\"ActualTotalTokens\" IS NULL AND \"OverageInputTokens\" = 0 AND " +
                "\"OverageOutputTokens\" = 0 AND \"OverageTotalTokens\" = 0) OR " +
                "(\"ActualTotalTokens\" IS NOT NULL AND " +
                "\"OverageInputTokens\" = GREATEST(\"ActualInputTokens\" - \"ReservedInputTokens\", 0) AND " +
                "\"OverageOutputTokens\" = GREATEST(\"ActualOutputTokens\" - \"ReservedOutputTokens\", 0) AND " +
                "\"OverageTotalTokens\" = GREATEST(\"ActualTotalTokens\" - \"ReservedTotalTokens\", 0)))");
            table.HasCheckConstraint("CK_AiUsageReservations_StateTimestamps",
                "(\"Status\" = 0 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR " +
                "(\"Status\" = 1 AND \"SettledAt\" IS NOT NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NOT NULL) OR " +
                "(\"Status\" = 2 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NOT NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR " +
                "(\"Status\" IN (3, 4) AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NOT NULL AND \"ReconciliationReason\" IS NOT NULL)");
            table.HasCheckConstraint("CK_AiUsageReservations_ReconciliationReason",
                "\"ReconciliationReason\" IS NULL OR \"ReconciliationReason\" IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_AiUsageReservations_ReconciliationLease",
                "(\"ReconciliationLeaseOwner\" IS NULL AND \"ReconciliationLeaseExpiresAt\" IS NULL) OR " +
                "(\"Status\" IN (3, 4) AND \"ReconciliationLeaseOwner\" IS NOT NULL AND " +
                "\"ReconciliationLeaseOwner\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ReconciliationLeaseExpiresAt\" IS NOT NULL AND " +
                "\"ReconciliationLeaseExpiresAt\" > \"ReconciliationRequiredAt\")");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.AttemptKey).IsRequired().HasMaxLength(100);
        builder.Property(item => item.ProviderName).IsRequired(false).HasMaxLength(100);
        builder.Property(item => item.ModelName).IsRequired(false).HasMaxLength(200);
        builder.Property(item => item.ProviderRequestId).IsRequired(false).HasMaxLength(200);
        builder.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.SettledAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.ReleasedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.ReconciliationRequiredAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.ReconciliationReason).HasConversion<int?>().IsRequired(false);
        builder.Property(item => item.ReconciliationLeaseOwner).IsRequired(false);
        builder.Property(item => item.ReconciliationLeaseExpiresAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.Version).IsRowVersion();
        builder.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(item => new { item.DraftId, item.AttemptCount }).IsUnique();
        builder.HasIndex(item => item.AttemptKey).IsUnique();
        builder.HasIndex(item => new { item.Status, item.ExpiresAt });
        builder.HasIndex(item => new
        {
            item.Status,
            item.ReconciliationRequiredAt,
            item.ReconciliationLeaseExpiresAt,
            item.Id
        });
        builder.HasIndex(item => item.ProviderRequestId)
            .HasFilter("\"ProviderRequestId\" IS NOT NULL");
        builder.HasIndex(item => item.UserId);
    }
}
