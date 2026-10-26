using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class TightenOwnershipCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses",
                sql: "(\"JobPostingId\" IS NULL AND \"JobPostingUserId\" IS NULL) OR (\"JobPostingId\" IS NOT NULL AND \"JobPostingUserId\" IS NOT NULL AND \"JobPostingUserId\" = \"UserId\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications",
                sql: "(\"ResumeId\" IS NULL AND \"ResumeUserId\" IS NULL) OR (\"ResumeId\" IS NOT NULL AND \"ResumeUserId\" IS NOT NULL AND \"ResumeUserId\" = \"UserId\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeAnalyses_JobPostingOwnership",
                table: "ResumeAnalyses",
                sql: "(\"JobPostingId\" IS NULL AND \"JobPostingUserId\" IS NULL) OR (\"JobPostingId\" IS NOT NULL AND \"JobPostingUserId\" = \"UserId\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobApplications_ResumeOwnership",
                table: "JobApplications",
                sql: "(\"ResumeId\" IS NULL AND \"ResumeUserId\" IS NULL) OR (\"ResumeId\" IS NOT NULL AND \"ResumeUserId\" = \"UserId\")");
        }
    }
}
