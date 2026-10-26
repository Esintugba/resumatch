using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceJobPostingInputHash",
                table: "ResumeAnalyses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceJobPostingSkillSetHash",
                table: "ResumeAnalyses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceJobPostingVersion",
                table: "ResumeAnalyses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingInputHash",
                table: "ResumeAnalyses",
                sql: "\"SourceJobPostingInputHash\" IS NULL OR \"SourceJobPostingInputHash\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingSkillSetHash",
                table: "ResumeAnalyses",
                sql: "\"SourceJobPostingSkillSetHash\" IS NULL OR \"SourceJobPostingSkillSetHash\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingVersion",
                table: "ResumeAnalyses",
                sql: "\"SourceJobPostingVersion\" IS NULL OR \"SourceJobPostingVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_SourceProvenanceCompleteness",
                table: "ResumeAnalyses",
                sql: "(\"SourceJobPostingVersion\" IS NULL AND \"SourceJobPostingInputHash\" IS NULL AND \"SourceJobPostingSkillSetHash\" IS NULL) OR (\"SourceJobPostingVersion\" IS NOT NULL AND \"SourceJobPostingInputHash\" IS NOT NULL AND \"SourceJobPostingSkillSetHash\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingInputHash",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingSkillSetHash",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_SourceJobPostingVersion",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_SourceProvenanceCompleteness",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "SourceJobPostingInputHash",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "SourceJobPostingSkillSetHash",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "SourceJobPostingVersion",
                table: "ResumeAnalyses");
        }
    }
}
