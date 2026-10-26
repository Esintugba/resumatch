using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceNonNegativeSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeSkills_SortOrder",
                table: "ResumeSkills",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeProjects_SortOrder",
                table: "ResumeProjects",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeLanguages_SortOrder",
                table: "ResumeLanguages",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeExperiences_SortOrder",
                table: "ResumeExperiences",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeEducations_SortOrder",
                table: "ResumeEducations",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResumeCertifications_SortOrder",
                table: "ResumeCertifications",
                sql: "\"SortOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExperienceBullets_SortOrder",
                table: "ExperienceBullets",
                sql: "\"SortOrder\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeSkills_SortOrder",
                table: "ResumeSkills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeProjects_SortOrder",
                table: "ResumeProjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeLanguages_SortOrder",
                table: "ResumeLanguages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeExperiences_SortOrder",
                table: "ResumeExperiences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeEducations_SortOrder",
                table: "ResumeEducations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResumeCertifications_SortOrder",
                table: "ResumeCertifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExperienceBullets_SortOrder",
                table: "ExperienceBullets");
        }
    }
}
