using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributedProviderProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderCircuitStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderScope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    OpenUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastFailureSequence = table.Column<long>(type: "bigint", nullable: false),
                    ActiveProbes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderCircuitStates", x => x.Id);
                    table.CheckConstraint("CK_AiProviderCircuitStates_Counters", "\"ConsecutiveFailures\" >= 0 AND \"Generation\" >= 0 AND \"NextSequence\" >= 0 AND \"LastFailureSequence\" >= 0 AND \"LastFailureSequence\" <= \"NextSequence\" AND \"ActiveProbes\" BETWEEN 0 AND 10");
                    table.CheckConstraint("CK_AiProviderCircuitStates_Identity", "length(btrim(\"ProviderScope\")) > 0");
                    table.CheckConstraint("CK_AiProviderCircuitStates_Lifecycle", "(\"Status\" = 0 AND \"OpenUntil\" IS NULL AND \"ActiveProbes\" = 0) OR (\"Status\" = 1 AND \"OpenUntil\" IS NOT NULL AND \"ActiveProbes\" = 0) OR (\"Status\" = 2 AND \"OpenUntil\" IS NULL)");
                    table.CheckConstraint("CK_AiProviderCircuitStates_Status", "\"Status\" IN (0, 1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "AiProviderConcurrencyLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderScope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderConcurrencyLeases", x => x.Id);
                    table.CheckConstraint("CK_AiProviderConcurrencyLeases_Identity", "length(btrim(\"ProviderScope\")) > 0 AND \"OwnerToken\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AiProviderConcurrencyLeases_Lifecycle", "(\"Status\" = 0 AND \"ReleasedAt\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ReleasedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_AiProviderConcurrencyLeases_Status", "\"Status\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_AiProviderConcurrencyLeases_Time", "\"AcquiredAt\" = \"CreatedAt\" AND \"ExpiresAt\" > \"AcquiredAt\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCircuitStates_ProviderScope",
                table: "AiProviderCircuitStates",
                column: "ProviderScope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCircuitStates_Status_OpenUntil",
                table: "AiProviderCircuitStates",
                columns: new[] { "Status", "OpenUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConcurrencyLeases_CorrelationId",
                table: "AiProviderConcurrencyLeases",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConcurrencyLeases_OwnerToken",
                table: "AiProviderConcurrencyLeases",
                column: "OwnerToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConcurrencyLeases_ProviderScope_Status_ExpiresAt",
                table: "AiProviderConcurrencyLeases",
                columns: new[] { "ProviderScope", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProviderCircuitStates");

            migrationBuilder.DropTable(
                name: "AiProviderConcurrencyLeases");
        }
    }
}
