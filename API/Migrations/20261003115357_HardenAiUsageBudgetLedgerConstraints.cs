using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class HardenAiUsageBudgetLedgerConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_ActualUsage",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_Attempt",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservationPeriods_Amounts",
                table: "AiUsageReservationPeriods");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_ActualUsage",
                table: "AiUsageReservations",
                sql: "(\"ActualInputTokens\" IS NULL AND \"ActualOutputTokens\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"ActualInputTokens\" IS NOT NULL AND \"ActualOutputTokens\" IS NOT NULL AND \"ActualTotalTokens\" IS NOT NULL AND \"ActualInputTokens\" >= 0 AND \"ActualOutputTokens\" >= 0 AND \"ActualTotalTokens\" = \"ActualInputTokens\" + \"ActualOutputTokens\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_Attempt",
                table: "AiUsageReservations",
                sql: "\"AttemptCount\" > 0 AND \"ReservedRequestCount\" = 1 AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DraftId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"AttemptKey\")) > 0 AND \"ExpiresAt\" > \"CreatedAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservationPeriods_Amounts",
                table: "AiUsageReservationPeriods",
                sql: "\"ReservedRequestCount\" = 1 AND \"ReservedTokenCount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_ActualUsage",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_Attempt",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservationPeriods_Amounts",
                table: "AiUsageReservationPeriods");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_ActualUsage",
                table: "AiUsageReservations",
                sql: "(\"ActualInputTokens\" IS NULL AND \"ActualOutputTokens\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"ActualInputTokens\" >= 0 AND \"ActualOutputTokens\" >= 0 AND \"ActualTotalTokens\" = \"ActualInputTokens\" + \"ActualOutputTokens\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_Attempt",
                table: "AiUsageReservations",
                sql: "\"AttemptCount\" > 0 AND \"ReservedRequestCount\" > 0 AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DraftId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"AttemptKey\")) > 0 AND \"ExpiresAt\" > \"CreatedAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservationPeriods_Amounts",
                table: "AiUsageReservationPeriods",
                sql: "\"ReservedRequestCount\" > 0 AND \"ReservedTokenCount\" >= 0");
        }
    }
}
