using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceResumeHierarchyIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Resumes_Resumes_SourceResumeId",
                table: "Resumes");

            migrationBuilder.AddColumn<bool>(
                name: "SourceResumeIsMaster",
                table: "Resumes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Resumes_Id_UserId_IsMasterResume",
                table: "Resumes",
                columns: new[] { "Id", "UserId", "IsMasterResume" });

            migrationBuilder.CreateIndex(
                name: "IX_Resumes_SourceResumeId_UserId_SourceResumeIsMaster",
                table: "Resumes",
                columns: new[] { "SourceResumeId", "UserId", "SourceResumeIsMaster" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Resumes_SourceMustBeMaster",
                table: "Resumes",
                sql: "(\"SourceResumeId\" IS NULL AND \"SourceResumeIsMaster\" IS NULL) OR (\"SourceResumeId\" IS NOT NULL AND \"SourceResumeIsMaster\" IS TRUE AND \"IsMasterResume\" IS FALSE)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Resumes_SourceNotSelf",
                table: "Resumes",
                sql: "\"SourceResumeId\" IS NULL OR \"SourceResumeId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Resumes_Resumes_SourceResumeId_UserId_SourceResumeIsMaster",
                table: "Resumes",
                columns: new[] { "SourceResumeId", "UserId", "SourceResumeIsMaster" },
                principalTable: "Resumes",
                principalColumns: new[] { "Id", "UserId", "IsMasterResume" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Resumes_Resumes_SourceResumeId_UserId_SourceResumeIsMaster",
                table: "Resumes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Resumes_Id_UserId_IsMasterResume",
                table: "Resumes");

            migrationBuilder.DropIndex(
                name: "IX_Resumes_SourceResumeId_UserId_SourceResumeIsMaster",
                table: "Resumes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Resumes_SourceMustBeMaster",
                table: "Resumes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Resumes_SourceNotSelf",
                table: "Resumes");

            migrationBuilder.DropColumn(
                name: "SourceResumeIsMaster",
                table: "Resumes");

            migrationBuilder.AddForeignKey(
                name: "FK_Resumes_Resumes_SourceResumeId",
                table: "Resumes",
                column: "SourceResumeId",
                principalTable: "Resumes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
