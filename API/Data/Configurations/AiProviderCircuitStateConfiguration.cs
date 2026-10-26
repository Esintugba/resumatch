using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public sealed class AiProviderCircuitStateConfiguration :
    IEntityTypeConfiguration<AiProviderCircuitState>
{
    public void Configure(EntityTypeBuilder<AiProviderCircuitState> builder)
    {
        builder.ToTable("AiProviderCircuitStates", table =>
        {
            table.HasCheckConstraint("CK_AiProviderCircuitStates_Status",
                "\"Status\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_AiProviderCircuitStates_Counters",
                "\"ConsecutiveFailures\" >= 0 AND \"Generation\" >= 0 AND " +
                "\"NextSequence\" >= 0 AND \"LastFailureSequence\" >= 0 AND " +
                "\"LastFailureSequence\" <= \"NextSequence\" AND " +
                "\"ActiveProbes\" BETWEEN 0 AND 10");
            table.HasCheckConstraint("CK_AiProviderCircuitStates_Lifecycle",
                "(\"Status\" = 0 AND \"OpenUntil\" IS NULL AND \"ActiveProbes\" = 0) OR " +
                "(\"Status\" = 1 AND \"OpenUntil\" IS NOT NULL AND \"ActiveProbes\" = 0) OR " +
                "(\"Status\" = 2 AND \"OpenUntil\" IS NULL)");
            table.HasCheckConstraint("CK_AiProviderCircuitStates_Identity",
                "length(btrim(\"ProviderScope\")) > 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ProviderScope).IsRequired().HasMaxLength(100);
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.OpenUntil).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(item => item.ProviderScope).IsUnique();
        builder.HasIndex(item => new { item.Status, item.OpenUntil });
    }
}
