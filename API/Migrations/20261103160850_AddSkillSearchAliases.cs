using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillSearchAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "SearchAliases",
                table: "Skills",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.Sql(AddCatalogV2Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveCatalogV2Sql);

            migrationBuilder.DropColumn(
                name: "SearchAliases",
                table: "Skills");
        }
    }
}
