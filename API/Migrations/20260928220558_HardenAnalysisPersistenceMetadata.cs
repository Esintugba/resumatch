using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class HardenAnalysisPersistenceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "ResumeAnalyses"
                SET "AnalyzerVersion" = 'legacy-unknown'
                WHERE "AnalyzerVersion" IS NULL
                   OR "AnalyzerVersion" !~ '[^[:space:]]';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "AnalyzerVersion",
                table: "ResumeAnalyses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleCode",
                table: "AnalysisRecommendations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "AnalysisRecommendations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_AnalyzerVersion_NotBlank",
                table: "ResumeAnalyses",
                sql: "\"AnalyzerVersion\" ~ '[^[:space:]]'");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRecommendations_ResumeAnalysisId_SortOrder",
                table: "AnalysisRecommendations",
                columns: new[] { "ResumeAnalysisId", "SortOrder" },
                unique: true,
                filter: "\"SortOrder\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AnalysisRecommendations_SortOrder",
                table: "AnalysisRecommendations",
                sql: "\"SortOrder\" IS NULL OR \"SortOrder\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_AnalyzerVersion_NotBlank",
                table: "ResumeAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisRecommendations_ResumeAnalysisId_SortOrder",
                table: "AnalysisRecommendations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AnalysisRecommendations_SortOrder",
                table: "AnalysisRecommendations");

            // Rolling back removes metadata written after this migration.
            // AnalyzerVersion values, including legacy-unknown, are retained
            // because their previous null state cannot be reconstructed safely.
            migrationBuilder.DropColumn(
                name: "RuleCode",
                table: "AnalysisRecommendations");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "AnalysisRecommendations");

            migrationBuilder.AlterColumn<string>(
                name: "AnalyzerVersion",
                table: "ResumeAnalyses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
