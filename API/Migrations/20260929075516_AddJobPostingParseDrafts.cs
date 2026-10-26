using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingParseDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPostingParseDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SourceJobPostingVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceDescriptionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceDescription = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    ParserVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPostingParseDrafts", x => x.Id);
                    table.CheckConstraint("CK_JobPostingParseDrafts_AttemptCount", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_JobPostingParseDrafts_ParserVersion_NotBlank", "\"ParserVersion\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_JobPostingParseDrafts_SourceDescriptionHash", "\"SourceDescriptionHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_JobPostingParseDrafts_SourceJobPostingVersion", "\"SourceJobPostingVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_JobPostingParseDrafts_JobPostings_JobPostingId_UserId",
                        columns: x => new { x.JobPostingId, x.UserId },
                        principalTable: "JobPostings",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobPostingParseDraftSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingParseDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CanonicalKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ResolvedSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequirementType = table.Column<int>(type: "integer", nullable: false),
                    ProposedImportanceScore = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPostingParseDraftSkills", x => x.Id);
                    table.CheckConstraint("CK_JobPostingParseDraftSkills_CanonicalKey_NotBlank", "\"CanonicalKey\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_JobPostingParseDraftSkills_Confidence", "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
                    table.CheckConstraint("CK_JobPostingParseDraftSkills_ProposedImportanceScore", "\"ProposedImportanceScore\" >= 0 AND \"ProposedImportanceScore\" <= 100");
                    table.CheckConstraint("CK_JobPostingParseDraftSkills_Resolution", "(\"ResolutionStatus\" IN (0, 1) AND \"ResolvedSkillId\" IS NOT NULL) OR (\"ResolutionStatus\" IN (2, 3) AND \"ResolvedSkillId\" IS NULL)");
                    table.CheckConstraint("CK_JobPostingParseDraftSkills_SortOrder", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_JobPostingParseDraftSkills_JobPostingParseDrafts_JobPosting~",
                        column: x => x.JobPostingParseDraftId,
                        principalTable: "JobPostingParseDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobPostingParseDraftSkills_Skills_ResolvedSkillId",
                        column: x => x.ResolvedSkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobPostingParseDraftSkillEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingParseDraftSkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Start = table.Column<int>(type: "integer", nullable: false),
                    Length = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPostingParseDraftSkillEvidences", x => x.Id);
                    table.CheckConstraint("CK_JobPostingParseDraftSkillEvidences_Length", "\"Length\" > 0");
                    table.CheckConstraint("CK_JobPostingParseDraftSkillEvidences_Start", "\"Start\" >= 0");
                    table.ForeignKey(
                        name: "FK_JobPostingParseDraftSkillEvidences_JobPostingParseDraftSkil~",
                        column: x => x.JobPostingParseDraftSkillId,
                        principalTable: "JobPostingParseDraftSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDrafts_JobPostingId_SourceDescriptionHash_Pa~",
                table: "JobPostingParseDrafts",
                columns: new[] { "JobPostingId", "SourceDescriptionHash", "ParserVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDrafts_JobPostingId_UserId",
                table: "JobPostingParseDrafts",
                columns: new[] { "JobPostingId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDraftSkillEvidences_JobPostingParseDraftSkil~",
                table: "JobPostingParseDraftSkillEvidences",
                columns: new[] { "JobPostingParseDraftSkillId", "Start", "Length" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDraftSkills_JobPostingParseDraftId_Canonical~",
                table: "JobPostingParseDraftSkills",
                columns: new[] { "JobPostingParseDraftId", "CanonicalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDraftSkills_JobPostingParseDraftId_SortOrder",
                table: "JobPostingParseDraftSkills",
                columns: new[] { "JobPostingParseDraftId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPostingParseDraftSkills_ResolvedSkillId",
                table: "JobPostingParseDraftSkills",
                column: "ResolvedSkillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPostingParseDraftSkillEvidences");

            migrationBuilder.DropTable(
                name: "JobPostingParseDraftSkills");

            migrationBuilder.DropTable(
                name: "JobPostingParseDrafts");
        }
    }
}
