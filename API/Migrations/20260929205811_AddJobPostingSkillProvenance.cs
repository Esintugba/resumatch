using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingSkillProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvalidatedAt",
                table: "JobPostingSkills",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvenanceStatus",
                table: "JobPostingSkills",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceParseDraftId",
                table: "JobPostingSkills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceParseDraftSkillId",
                table: "JobPostingSkills",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "JobPostingSkills"
                SET "ProvenanceStatus" = CASE
                    WHEN "IsAiDetected" THEN 4
                    ELSE 0
                END;

                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "JobPostingSkills"
                        WHERE "ProvenanceStatus" IS NULL
                           OR ("IsAiDetected" AND "ProvenanceStatus" <> 4)
                           OR (NOT "IsAiDetected" AND "ProvenanceStatus" <> 0)
                    ) THEN
                        RAISE EXCEPTION
                            'JobPostingSkill provenance backfill could not be verified.';
                    END IF;
                END
                $migration$;
                """);

            // The temporary database default keeps an older application binary
            // capable of inserting its only production-generated AI skill form.
            // Such rows are explicitly legacy/unknown, never falsely active.
            // A coordinated follow-up deployment should remove this default once
            // confirmation always writes complete provenance.
            migrationBuilder.AlterColumn<int>(
                name: "ProvenanceStatus",
                table: "JobPostingSkills",
                type: "integer",
                nullable: false,
                defaultValue: 4,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_JobPostingParseDraftSkills_Id_JobPostingParseDraftId",
                table: "JobPostingParseDraftSkills",
                columns: new[] { "Id", "JobPostingParseDraftId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_JobPostingParseDrafts_Id_JobPostingId",
                table: "JobPostingParseDrafts",
                columns: new[] { "Id", "JobPostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingSkills_JobPostingId_ProvenanceStatus",
                table: "JobPostingSkills",
                columns: new[] { "JobPostingId", "ProvenanceStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingSkills_SourceParseDraftId_JobPostingId",
                table: "JobPostingSkills",
                columns: new[] { "SourceParseDraftId", "JobPostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingSkills_SourceParseDraftSkillId",
                table: "JobPostingSkills",
                column: "SourceParseDraftSkillId",
                unique: true,
                filter: "\"SourceParseDraftSkillId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingSkills_SourceParseDraftSkillId_SourceParseDraftId",
                table: "JobPostingSkills",
                columns: new[] { "SourceParseDraftSkillId", "SourceParseDraftId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingSkills_ProvenanceInvariant",
                table: "JobPostingSkills",
                sql: "(\"ProvenanceStatus\" = 0 AND \"SourceParseDraftId\" IS NULL AND \"SourceParseDraftSkillId\" IS NULL AND \"InvalidatedAt\" IS NULL AND NOT \"IsAiDetected\") OR (\"ProvenanceStatus\" = 1 AND \"SourceParseDraftId\" IS NOT NULL AND \"SourceParseDraftSkillId\" IS NOT NULL AND \"InvalidatedAt\" IS NULL AND \"IsAiDetected\") OR (\"ProvenanceStatus\" = 2 AND \"SourceParseDraftId\" IS NOT NULL AND \"SourceParseDraftSkillId\" IS NOT NULL AND \"InvalidatedAt\" IS NOT NULL AND \"IsAiDetected\") OR (\"ProvenanceStatus\" = 3 AND \"SourceParseDraftId\" IS NOT NULL AND \"SourceParseDraftSkillId\" IS NOT NULL AND \"InvalidatedAt\" IS NULL AND \"IsAiDetected\") OR (\"ProvenanceStatus\" = 4 AND \"SourceParseDraftId\" IS NULL AND \"SourceParseDraftSkillId\" IS NULL AND \"InvalidatedAt\" IS NULL AND \"IsAiDetected\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingSkills_ProvenanceStatus",
                table: "JobPostingSkills",
                sql: "\"ProvenanceStatus\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "UX_JobPostingParseDraftSkills_ProvenanceIdentity",
                table: "JobPostingParseDraftSkills",
                columns: new[] { "Id", "JobPostingParseDraftId", "ResolvedSkillId" },
                unique: true);

            // EF Core cannot use nullable ResolvedSkillId in an alternate key.
            // PostgreSQL can safely enforce this FK because the supporting
            // unique index exists and every referencing row has non-null source
            // identifiers and a non-null SkillId. An unresolved candidate
            // therefore cannot satisfy the referenced triple.
            migrationBuilder.Sql(
                """
                ALTER TABLE "JobPostingSkills"
                ADD CONSTRAINT "FK_JobPostingSkills_SourceDraftSkill_Draft_Skill"
                FOREIGN KEY (
                    "SourceParseDraftSkillId",
                    "SourceParseDraftId",
                    "SkillId")
                REFERENCES "JobPostingParseDraftSkills" (
                    "Id",
                    "JobPostingParseDraftId",
                    "ResolvedSkillId")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPostingSkills_JobPostingParseDraftSkills_SourceParseDraf~",
                table: "JobPostingSkills",
                columns: new[] { "SourceParseDraftSkillId", "SourceParseDraftId" },
                principalTable: "JobPostingParseDraftSkills",
                principalColumns: new[] { "Id", "JobPostingParseDraftId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPostingSkills_JobPostingParseDrafts_SourceParseDraftId_J~",
                table: "JobPostingSkills",
                columns: new[] { "SourceParseDraftId", "JobPostingId" },
                principalTable: "JobPostingParseDrafts",
                principalColumns: new[] { "Id", "JobPostingId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "JobPostingSkills"
                DROP CONSTRAINT "FK_JobPostingSkills_SourceDraftSkill_Draft_Skill";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_JobPostingSkills_JobPostingParseDraftSkills_SourceParseDraf~",
                table: "JobPostingSkills");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPostingSkills_JobPostingParseDrafts_SourceParseDraftId_J~",
                table: "JobPostingSkills");

            migrationBuilder.DropIndex(
                name: "IX_JobPostingSkills_JobPostingId_ProvenanceStatus",
                table: "JobPostingSkills");

            migrationBuilder.DropIndex(
                name: "IX_JobPostingSkills_SourceParseDraftId_JobPostingId",
                table: "JobPostingSkills");

            migrationBuilder.DropIndex(
                name: "IX_JobPostingSkills_SourceParseDraftSkillId",
                table: "JobPostingSkills");

            migrationBuilder.DropIndex(
                name: "IX_JobPostingSkills_SourceParseDraftSkillId_SourceParseDraftId",
                table: "JobPostingSkills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingSkills_ProvenanceInvariant",
                table: "JobPostingSkills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingSkills_ProvenanceStatus",
                table: "JobPostingSkills");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_JobPostingParseDraftSkills_Id_JobPostingParseDraftId",
                table: "JobPostingParseDraftSkills");

            migrationBuilder.DropIndex(
                name: "UX_JobPostingParseDraftSkills_ProvenanceIdentity",
                table: "JobPostingParseDraftSkills");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_JobPostingParseDrafts_Id_JobPostingId",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "InvalidatedAt",
                table: "JobPostingSkills");

            migrationBuilder.DropColumn(
                name: "ProvenanceStatus",
                table: "JobPostingSkills");

            migrationBuilder.DropColumn(
                name: "SourceParseDraftId",
                table: "JobPostingSkills");

            migrationBuilder.DropColumn(
                name: "SourceParseDraftSkillId",
                table: "JobPostingSkills");
        }
    }
}
