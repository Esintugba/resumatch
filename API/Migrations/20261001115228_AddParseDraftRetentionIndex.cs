using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddParseDraftRetentionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_JobPostingParseDrafts_Retention"
                ON "JobPostingParseDrafts"
                (
                    (CASE
                        WHEN "Status" = 4 THEN "ConfirmedAt"
                        WHEN "Status" = 3 THEN "CompletedAt"
                        WHEN "Status" = 5 THEN COALESCE("CompletedAt", "UpdatedAt")
                        WHEN "Status" IN (6, 7) THEN "UpdatedAt"
                    END),
                    "Id"
                )
                WHERE "Status" IN (3, 4, 5, 6, 7);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX "IX_JobPostingParseDrafts_Retention";
                """);
        }
    }
}
