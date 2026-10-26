using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class HardenAiUsageReconciliationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_StateTimestamps",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageBudgetPeriods_Bounded",
                table: "AiUsageBudgetPeriods");

            migrationBuilder.AddColumn<long>(
                name: "OverageInputTokens",
                table: "AiUsageReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OverageOutputTokens",
                table: "AiUsageReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OverageTotalTokens",
                table: "AiUsageReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestId",
                table: "AiUsageReservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciliationLeaseExpiresAt",
                table: "AiUsageReservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationLeaseOwner",
                table: "AiUsageReservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationReason",
                table: "AiUsageReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AiUsageReservations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.Sql(
                """
                UPDATE "AiUsageReservations"
                SET "ReconciliationReason" = CASE
                        WHEN "Status" = 3 THEN 2
                        WHEN "ActualTotalTokens" IS NOT NULL THEN 3
                        ELSE 1
                    END,
                    "OverageInputTokens" = CASE
                        WHEN "ActualInputTokens" IS NULL THEN 0
                        ELSE GREATEST("ActualInputTokens" - "ReservedInputTokens", 0)
                    END,
                    "OverageOutputTokens" = CASE
                        WHEN "ActualOutputTokens" IS NULL THEN 0
                        ELSE GREATEST("ActualOutputTokens" - "ReservedOutputTokens", 0)
                    END,
                    "OverageTotalTokens" = CASE
                        WHEN "ActualTotalTokens" IS NULL THEN 0
                        ELSE GREATEST("ActualTotalTokens" - "ReservedTotalTokens", 0)
                    END
                WHERE "Status" IN (3, 4);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_ProviderRequestId",
                table: "AiUsageReservations",
                column: "ProviderRequestId",
                filter: "\"ProviderRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_Status_ReconciliationRequiredAt_Reconci~",
                table: "AiUsageReservations",
                columns: new[] { "Status", "ReconciliationRequiredAt", "ReconciliationLeaseExpiresAt", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_Overage",
                table: "AiUsageReservations",
                sql: "\"OverageInputTokens\" >= 0 AND \"OverageOutputTokens\" >= 0 AND \"OverageTotalTokens\" >= 0 AND ((\"ActualTotalTokens\" IS NULL AND \"OverageInputTokens\" = 0 AND \"OverageOutputTokens\" = 0 AND \"OverageTotalTokens\" = 0) OR (\"ActualTotalTokens\" IS NOT NULL AND \"OverageInputTokens\" = GREATEST(\"ActualInputTokens\" - \"ReservedInputTokens\", 0) AND \"OverageOutputTokens\" = GREATEST(\"ActualOutputTokens\" - \"ReservedOutputTokens\", 0) AND \"OverageTotalTokens\" = GREATEST(\"ActualTotalTokens\" - \"ReservedTotalTokens\", 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_ReconciliationLease",
                table: "AiUsageReservations",
                sql: "(\"ReconciliationLeaseOwner\" IS NULL AND \"ReconciliationLeaseExpiresAt\" IS NULL) OR (\"Status\" IN (3, 4) AND \"ReconciliationLeaseOwner\" IS NOT NULL AND \"ReconciliationLeaseOwner\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciliationLeaseExpiresAt\" IS NOT NULL AND \"ReconciliationLeaseExpiresAt\" > \"ReconciliationRequiredAt\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_ReconciliationReason",
                table: "AiUsageReservations",
                sql: "\"ReconciliationReason\" IS NULL OR \"ReconciliationReason\" IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_StateTimestamps",
                table: "AiUsageReservations",
                sql: "(\"Status\" = 0 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"Status\" = 1 AND \"SettledAt\" IS NOT NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NOT NULL) OR (\"Status\" = 2 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NOT NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ReconciliationReason\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"Status\" IN (3, 4) AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NOT NULL AND \"ReconciliationReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiUsageReservations_ProviderRequestId",
                table: "AiUsageReservations");

            migrationBuilder.DropIndex(
                name: "IX_AiUsageReservations_Status_ReconciliationRequiredAt_Reconci~",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_Overage",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_ReconciliationLease",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_ReconciliationReason",
                table: "AiUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiUsageReservations_StateTimestamps",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "OverageInputTokens",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "OverageOutputTokens",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "OverageTotalTokens",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "ProviderRequestId",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "ReconciliationLeaseExpiresAt",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "ReconciliationLeaseOwner",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "ReconciliationReason",
                table: "AiUsageReservations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AiUsageReservations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageReservations_StateTimestamps",
                table: "AiUsageReservations",
                sql: "(\"Status\" = 0 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL) OR (\"Status\" = 1 AND \"SettledAt\" IS NOT NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ActualTotalTokens\" IS NOT NULL) OR (\"Status\" = 2 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NOT NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"Status\" IN (3, 4) AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NOT NULL)");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM "AiUsageBudgetPeriods"
                        WHERE "ReservedRequests" + "ConsumedRequests" > "RequestLimit"
                           OR "ReservedTokens" + "ConsumedTotalTokens" > "TokenLimit"
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade while AI usage overage accounting exists.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiUsageBudgetPeriods_Bounded",
                table: "AiUsageBudgetPeriods",
                sql: "\"ReservedRequests\" + \"ConsumedRequests\" <= \"RequestLimit\" AND \"ReservedTokens\" + \"ConsumedTotalTokens\" <= \"TokenLimit\"");
        }
    }
}
