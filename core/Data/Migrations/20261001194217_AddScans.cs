using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace securitycheck_portal.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Scans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatternId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Pattern = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FailureDetail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScannedTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScannedCommit = table.Column<string>(type: "character(40)", fixedLength: true, maxLength: 40, nullable: true),
                    TrivyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TrivyDbUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MissingLockFiles = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanFindings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScanId = table.Column<long>(type: "bigint", nullable: false),
                    Library = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstalledVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VulnerabilityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FixedVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Targets = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanFindings_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanFindings_ScanId_Library_InstalledVersion_VulnerabilityId",
                table: "ScanFindings",
                columns: new[] { "ScanId", "Library", "InstalledVersion", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scans_PatternId",
                table: "Scans",
                column: "PatternId",
                unique: true,
                filter: "\"Status\" IN ('Queued','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_PatternId_RequestedAt",
                table: "Scans",
                columns: new[] { "PatternId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanFindings");

            migrationBuilder.DropTable(
                name: "Scans");
        }
    }
}
