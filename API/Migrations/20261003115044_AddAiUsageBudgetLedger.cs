using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageBudgetLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageBudgetPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestLimit = table.Column<long>(type: "bigint", nullable: false),
                    TokenLimit = table.Column<long>(type: "bigint", nullable: false),
                    ReservedRequests = table.Column<long>(type: "bigint", nullable: false),
                    ReservedTokens = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedRequests = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedTotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageBudgetPeriods", x => x.Id);
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_Bounded", "\"ReservedRequests\" + \"ConsumedRequests\" <= \"RequestLimit\" AND \"ReservedTokens\" + \"ConsumedTotalTokens\" <= \"TokenLimit\"");
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_Counters", "\"ReservedRequests\" >= 0 AND \"ReservedTokens\" >= 0 AND \"ConsumedRequests\" >= 0 AND \"ConsumedInputTokens\" >= 0 AND \"ConsumedOutputTokens\" >= 0 AND \"ConsumedTotalTokens\" >= 0 AND \"ConsumedTotalTokens\" = \"ConsumedInputTokens\" + \"ConsumedOutputTokens\"");
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_Limits", "\"RequestLimit\" >= 0 AND \"TokenLimit\" >= 0");
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_PeriodRange", "\"PeriodEnd\" > \"PeriodStart\"");
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_PeriodType", "\"PeriodType\" IN (0, 1)");
                    table.CheckConstraint("CK_AiUsageBudgetPeriods_Scope", "(\"ScopeType\" = 0 AND \"UserId\" IS NULL) OR (\"ScopeType\" = 1 AND \"UserId\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "AiUsageReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReservedRequestCount = table.Column<int>(type: "integer", nullable: false),
                    ReservedInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ReservedOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ReservedTotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconciliationRequiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ActualOutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ActualTotalTokens = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageReservations", x => x.Id);
                    table.CheckConstraint("CK_AiUsageReservations_ActualUsage", "(\"ActualInputTokens\" IS NULL AND \"ActualOutputTokens\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"ActualInputTokens\" >= 0 AND \"ActualOutputTokens\" >= 0 AND \"ActualTotalTokens\" = \"ActualInputTokens\" + \"ActualOutputTokens\")");
                    table.CheckConstraint("CK_AiUsageReservations_Attempt", "\"AttemptCount\" > 0 AND \"ReservedRequestCount\" > 0 AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DraftId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"AttemptKey\")) > 0 AND \"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_AiUsageReservations_ReservedTokens", "\"ReservedInputTokens\" >= 0 AND \"ReservedOutputTokens\" >= 0 AND \"ReservedTotalTokens\" = \"ReservedInputTokens\" + \"ReservedOutputTokens\"");
                    table.CheckConstraint("CK_AiUsageReservations_StateTimestamps", "(\"Status\" = 0 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL) OR (\"Status\" = 1 AND \"SettledAt\" IS NOT NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ActualTotalTokens\" IS NOT NULL) OR (\"Status\" = 2 AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NOT NULL AND \"ReconciliationRequiredAt\" IS NULL AND \"ActualTotalTokens\" IS NULL) OR (\"Status\" IN (3, 4) AND \"SettledAt\" IS NULL AND \"ReleasedAt\" IS NULL AND \"ReconciliationRequiredAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_AiUsageReservations_Status", "\"Status\" IN (0, 1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "AiUsageReservationPeriods",
                columns: table => new
                {
                    AiUsageReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiUsageBudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedRequestCount = table.Column<int>(type: "integer", nullable: false),
                    ReservedTokenCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageReservationPeriods", x => new { x.AiUsageReservationId, x.AiUsageBudgetPeriodId });
                    table.CheckConstraint("CK_AiUsageReservationPeriods_Amounts", "\"ReservedRequestCount\" > 0 AND \"ReservedTokenCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AiUsageReservationPeriods_AiUsageBudgetPeriods_AiUsageBudge~",
                        column: x => x.AiUsageBudgetPeriodId,
                        principalTable: "AiUsageBudgetPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiUsageReservationPeriods_AiUsageReservations_AiUsageReserv~",
                        column: x => x.AiUsageReservationId,
                        principalTable: "AiUsageReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageBudgetPeriods_PeriodEnd",
                table: "AiUsageBudgetPeriods",
                column: "PeriodEnd");

            migrationBuilder.CreateIndex(
                name: "UX_AiUsageBudgetPeriods_System",
                table: "AiUsageBudgetPeriods",
                columns: new[] { "PeriodType", "PeriodStart" },
                unique: true,
                filter: "\"ScopeType\" = 0 AND \"UserId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AiUsageBudgetPeriods_User",
                table: "AiUsageBudgetPeriods",
                columns: new[] { "UserId", "PeriodType", "PeriodStart" },
                unique: true,
                filter: "\"ScopeType\" = 1 AND \"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservationPeriods_AiUsageBudgetPeriodId",
                table: "AiUsageReservationPeriods",
                column: "AiUsageBudgetPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_AttemptKey",
                table: "AiUsageReservations",
                column: "AttemptKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_DraftId_AttemptCount",
                table: "AiUsageReservations",
                columns: new[] { "DraftId", "AttemptCount" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_Status_ExpiresAt",
                table: "AiUsageReservations",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_UserId",
                table: "AiUsageReservations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageReservationPeriods");

            migrationBuilder.DropTable(
                name: "AiUsageBudgetPeriods");

            migrationBuilder.DropTable(
                name: "AiUsageReservations");
        }
    }
}
