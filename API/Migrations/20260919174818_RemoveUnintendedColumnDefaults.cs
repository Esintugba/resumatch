using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnintendedColumnDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Skills\" ALTER COLUMN \"NormalizedName\" DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE \"ResumeAnalyses\" ALTER COLUMN \"UserId\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Skills\" ALTER COLUMN \"NormalizedName\" SET DEFAULT '';");

            migrationBuilder.Sql(
                "ALTER TABLE \"ResumeAnalyses\" ALTER COLUMN \"UserId\" " +
                "SET DEFAULT '00000000-0000-0000-0000-000000000000'::uuid;");
        }
    }
}
