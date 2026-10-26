using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiUsageReservationPeriodConfiguration :
    IEntityTypeConfiguration<AiUsageReservationPeriod>
{
    public void Configure(EntityTypeBuilder<AiUsageReservationPeriod> builder)
    {
        builder.ToTable("AiUsageReservationPeriods", table =>
        {
            table.HasCheckConstraint("CK_AiUsageReservationPeriods_Amounts",
                "\"ReservedRequestCount\" = 1 AND \"ReservedTokenCount\" >= 0");
        });
        builder.HasKey(item => new
        {
            item.AiUsageReservationId,
            item.AiUsageBudgetPeriodId
        });
        builder.HasOne(item => item.Reservation)
            .WithMany(item => item.Periods)
            .HasForeignKey(item => item.AiUsageReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.BudgetPeriod)
            .WithMany(item => item.Reservations)
            .HasForeignKey(item => item.AiUsageBudgetPeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => item.AiUsageBudgetPeriodId);
    }
}
