using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributedCircuitProbeLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderCircuitProbeLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderScope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    OwnerToken = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderCircuitProbeLeases", x => x.Id);
                    table.CheckConstraint("CK_AiProviderCircuitProbeLeases_Identity", "length(btrim(\"ProviderScope\")) > 0 AND \"Generation\" >= 0 AND \"Sequence\" > 0 AND \"OwnerToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AiProviderCircuitProbeLeases_Lifecycle", "(\"Status\" = 0 AND \"CompletedAt\" IS NULL) OR (\"Status\" IN (1, 2) AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_AiProviderCircuitProbeLeases_Status", "\"Status\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_AiProviderCircuitProbeLeases_Timestamps", "\"ExpiresAt\" > \"AcquiredAt\" AND (\"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"AcquiredAt\")");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCircuitProbeLeases_OwnerToken",
                table: "AiProviderCircuitProbeLeases",
                column: "OwnerToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCircuitProbeLeases_ProviderScope_Generation_Seque~",
                table: "AiProviderCircuitProbeLeases",
                columns: new[] { "ProviderScope", "Generation", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCircuitProbeLeases_ProviderScope_Generation_Statu~",
                table: "AiProviderCircuitProbeLeases",
                columns: new[] { "ProviderScope", "Generation", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProviderCircuitProbeLeases");
        }
    }
}
