using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillCatalogV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameTr",
                table: "Skills",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNameTr",
                table: "Skills",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnlistedSkillsNote",
                table: "Resumes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(InsertCatalogSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeleteUnusedCatalogSql);

            migrationBuilder.DropColumn(
                name: "NameTr",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "NormalizedNameTr",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "UnlistedSkillsNote",
                table: "Resumes");
        }
    }
}
