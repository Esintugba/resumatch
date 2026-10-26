using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCrossUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_JobPostings_JobPostingId",
                table: "JobApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_Resumes_ResumeId",
                table: "JobApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeAnalyses_JobPostings_JobPostingId",
                table: "ResumeAnalyses");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeAnalyses_Resumes_ResumeId",
                table: "ResumeAnalyses");

            migrationBuilder.AddColumn<Guid>(
                name: "JobPostingUserId",
                table: "ResumeAnalyses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ResumeAnalyses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ResumeUserId",
                table: "JobApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Resumes_Id_UserId",
                table: "Resumes",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_JobPostings_Id_UserId",
                table: "JobPostings",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeAnalyses_JobPostingId_JobPostingUserId",
                table: "ResumeAnalyses",
                columns: new[] { "JobPostingId", "JobPostingUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeAnalyses_ResumeId_UserId",
                table: "ResumeAnalyses",
                columns: new[] { "ResumeId", "UserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses",
                sql: "(\"JobPostingId\" IS NULL AND \"JobPostingUserId\" IS NULL) OR (\"JobPostingId\" IS NOT NULL AND \"JobPostingUserId\" = \"UserId\")");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_JobPostingId_UserId",
                table: "JobApplications",
                columns: new[] { "JobPostingId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_ResumeId_ResumeUserId",
                table: "JobApplications",
                columns: new[] { "ResumeId", "ResumeUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications",
                sql: "(\"ResumeId\" IS NULL AND \"ResumeUserId\" IS NULL) OR (\"ResumeId\" IS NOT NULL AND \"ResumeUserId\" = \"UserId\")");

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_JobPostings_JobPostingId_UserId",
                table: "JobApplications",
                columns: new[] { "JobPostingId", "UserId" },
                principalTable: "JobPostings",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_Resumes_ResumeId_ResumeUserId",
                table: "JobApplications",
                columns: new[] { "ResumeId", "ResumeUserId" },
                principalTable: "Resumes",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeAnalyses_JobPostings_JobPostingId_JobPostingUserId",
                table: "ResumeAnalyses",
                columns: new[] { "JobPostingId", "JobPostingUserId" },
                principalTable: "JobPostings",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeAnalyses_Resumes_ResumeId_UserId",
                table: "ResumeAnalyses",
                columns: new[] { "ResumeId", "UserId" },
                principalTable: "Resumes",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_JobPostings_JobPostingId_UserId",
                table: "JobApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_Resumes_ResumeId_ResumeUserId",
                table: "JobApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeAnalyses_JobPostings_JobPostingId_JobPostingUserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeAnalyses_Resumes_ResumeId_UserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Resumes_Id_UserId",
                table: "Resumes");

            migrationBuilder.DropIndex(
                name: "IX_ResumeAnalyses_JobPostingId_JobPostingUserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_ResumeAnalyses_ResumeId_UserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_JobPostings_Id_UserId",
                table: "JobPostings");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_JobPostingId_UserId",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_ResumeId_ResumeUserId",
                table: "JobApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "JobPostingUserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ResumeUserId",
                table: "JobApplications");

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_JobPostings_JobPostingId",
                table: "JobApplications",
                column: "JobPostingId",
                principalTable: "JobPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_Resumes_ResumeId",
                table: "JobApplications",
                column: "ResumeId",
                principalTable: "Resumes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeAnalyses_JobPostings_JobPostingId",
                table: "ResumeAnalyses",
                column: "JobPostingId",
                principalTable: "JobPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeAnalyses_Resumes_ResumeId",
                table: "ResumeAnalyses",
                column: "ResumeId",
                principalTable: "Resumes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
