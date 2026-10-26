using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiUsageBudgetPeriodConfiguration :
    IEntityTypeConfiguration<AiUsageBudgetPeriod>
{
    public void Configure(EntityTypeBuilder<AiUsageBudgetPeriod> builder)
    {
        builder.ToTable("AiUsageBudgetPeriods", table =>
        {
            table.HasCheckConstraint("CK_AiUsageBudgetPeriods_Scope",
                "(\"ScopeType\" = 0 AND \"UserId\" IS NULL) OR " +
                "(\"ScopeType\" = 1 AND \"UserId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_AiUsageBudgetPeriods_PeriodType",
                "\"PeriodType\" IN (0, 1)");
            table.HasCheckConstraint("CK_AiUsageBudgetPeriods_PeriodRange",
                "\"PeriodEnd\" > \"PeriodStart\"");
            table.HasCheckConstraint("CK_AiUsageBudgetPeriods_Limits",
                "\"RequestLimit\" >= 0 AND \"TokenLimit\" >= 0");
            table.HasCheckConstraint("CK_AiUsageBudgetPeriods_Counters",
                "\"ReservedRequests\" >= 0 AND \"ReservedTokens\" >= 0 AND " +
                "\"ConsumedRequests\" >= 0 AND \"ConsumedInputTokens\" >= 0 AND " +
                "\"ConsumedOutputTokens\" >= 0 AND \"ConsumedTotalTokens\" >= 0 AND " +
                "\"ConsumedTotalTokens\" = \"ConsumedInputTokens\" + \"ConsumedOutputTokens\"");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ScopeType).HasConversion<int>().IsRequired();
        builder.Property(item => item.UserId).IsRequired(false);
        builder.Property(item => item.PeriodType).HasConversion<int>().IsRequired();
        builder.Property(item => item.PeriodStart).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.PeriodEnd).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.RequestLimit).IsRequired();
        builder.Property(item => item.TokenLimit).IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(item => new { item.PeriodType, item.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_AiUsageBudgetPeriods_System")
            .HasFilter("\"ScopeType\" = 0 AND \"UserId\" IS NULL");
        builder.HasIndex(item => new { item.UserId, item.PeriodType, item.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_AiUsageBudgetPeriods_User")
            .HasFilter("\"ScopeType\" = 1 AND \"UserId\" IS NOT NULL");
        builder.HasIndex(item => item.PeriodEnd);
    }
}
