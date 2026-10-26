using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class HardenJobPostingParseDraftLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "JobPostingParseDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "JobPostingParseDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "JobPostingParseDrafts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "JobPostingParseDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkillCatalogHash",
                table: "JobPostingParseDrafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceInputHash",
                table: "JobPostingParseDrafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTitle",
                table: "JobPostingParseDrafts",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "JobPostingParseDrafts"
                        WHERE "Status" = 1
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot harden JobPostingParseDrafts while legacy Processing rows lack lease metadata. Correct those rows manually before retrying the migration.';
                    END IF;
                END
                $migration$;

                -- Legacy drafts did not persist their historical source title.
                -- The current owned JobPosting title is the only recoverable
                -- best-effort value; SourceDescription remains unchanged.
                UPDATE "JobPostingParseDrafts" AS draft
                SET
                    "SourceTitle" = posting."Title",
                    "SourceInputHash" = encode(
                        sha256(
                            int4send(octet_length(convert_to(posting."Title", 'UTF8'))) ||
                            convert_to(posting."Title", 'UTF8') ||
                            int4send(octet_length(convert_to(draft."SourceDescription", 'UTF8'))) ||
                            convert_to(draft."SourceDescription", 'UTF8')
                        ),
                        'hex'
                    )
                FROM "JobPostings" AS posting
                WHERE posting."Id" = draft."JobPostingId"
                  AND posting."UserId" = draft."UserId";

                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "JobPostingParseDrafts"
                        WHERE "SourceTitle" IS NULL
                           OR "SourceTitle" !~ '[^[:space:]]'
                           OR "SourceInputHash" IS NULL
                           OR "SourceInputHash" !~ '^[0-9a-f]{64}$'
                    ) THEN
                        RAISE EXCEPTION
                            'JobPostingParseDraft source snapshot backfill could not be verified. Correct the affected rows manually before retrying the migration.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "JobPostingParseDrafts"
                        WHERE "Status" IN (0, 1, 2)
                        GROUP BY "JobPostingId", "SourceInputHash", "ParserVersion"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'Duplicate active JobPostingParseDraft fingerprints exist. Resolve them manually before retrying the migration.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "SourceInputHash",
                table: "JobPostingParseDrafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceTitle",
                table: "JobPostingParseDrafts",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDrafts_PendingClaim",
                table: "JobPostingParseDrafts",
                columns: new[] { "Status", "NextAttemptAt", "RequestedAt" },
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDrafts_ProcessingLease",
                table: "JobPostingParseDrafts",
                columns: new[] { "Status", "LeaseExpiresAt" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_JobPostingParseDrafts_ActiveFingerprint",
                table: "JobPostingParseDrafts",
                columns: new[] { "JobPostingId", "SourceInputHash", "ParserVersion" },
                unique: true,
                filter: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_LeaseOwner_NotBlank",
                table: "JobPostingParseDrafts",
                sql: "\"LeaseOwner\" IS NULL OR \"LeaseOwner\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_NextAttemptStatus",
                table: "JobPostingParseDrafts",
                sql: "\"NextAttemptAt\" IS NULL OR \"Status\" = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_ProcessingLease",
                table: "JobPostingParseDrafts",
                sql: "(\"Status\" = 1 AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"LastAttemptAt\" IS NOT NULL AND \"NextAttemptAt\" IS NULL AND \"LeaseExpiresAt\" > \"LastAttemptAt\") OR (\"Status\" <> 1 AND \"LeaseOwner\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_SkillCatalogHash",
                table: "JobPostingParseDrafts",
                sql: "\"SkillCatalogHash\" IS NULL OR \"SkillCatalogHash\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_SourceInputHash",
                table: "JobPostingParseDrafts",
                sql: "\"SourceInputHash\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_SourceTitle_NotBlank",
                table: "JobPostingParseDrafts",
                sql: "\"SourceTitle\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPostingParseDrafts_Status",
                table: "JobPostingParseDrafts",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobPostingParseDrafts_PendingClaim",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropIndex(
                name: "IX_JobPostingParseDrafts_ProcessingLease",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropIndex(
                name: "UX_JobPostingParseDrafts_ActiveFingerprint",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_LeaseOwner_NotBlank",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_NextAttemptStatus",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_ProcessingLease",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_SkillCatalogHash",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_SourceInputHash",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_SourceTitle_NotBlank",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPostingParseDrafts_Status",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "SkillCatalogHash",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "SourceInputHash",
                table: "JobPostingParseDrafts");

            migrationBuilder.DropColumn(
                name: "SourceTitle",
                table: "JobPostingParseDrafts");
        }
    }
}
