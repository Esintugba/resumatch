using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSkillNormalizedNameUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Skills_NormalizedName_Category",
                table: "Skills");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Skills",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_NormalizedName_Category",
                table: "Skills",
                columns: new[] { "NormalizedName", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Skills_NormalizedName_Category",
                table: "Skills");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Skills",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_NormalizedName_Category",
                table: "Skills",
                columns: new[] { "NormalizedName", "Category" },
                unique: true,
                filter: "\"NormalizedName\" IS NOT NULL");
        }
    }
}
