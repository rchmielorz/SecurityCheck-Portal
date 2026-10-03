using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace securitycheck_portal.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanUnscannedItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanUnscannedItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScanId = table.Column<long>(type: "bigint", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanUnscannedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanUnscannedItems_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanUnscannedItems_ScanId_Path",
                table: "ScanUnscannedItems",
                columns: new[] { "ScanId", "Path" },
                unique: true);

            // Keep the old data: one row per path, reason unknown, no detail (duplicates collapse on the unique index).
            migrationBuilder.Sql(
                "INSERT INTO \"ScanUnscannedItems\" (\"ScanId\", \"Path\", \"Reason\", \"Detail\") " +
                "SELECT s.\"Id\", left(p, 1024), 'Unknown', NULL " +
                "FROM \"Scans\" s, unnest(s.\"MissingLockFiles\") AS p " +
                "ON CONFLICT (\"ScanId\", \"Path\") DO NOTHING;");

            migrationBuilder.DropColumn(
                name: "MissingLockFiles",
                table: "Scans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "MissingLockFiles",
                table: "Scans",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql(
                "UPDATE \"Scans\" s SET \"MissingLockFiles\" = COALESCE(" +
                "(SELECT array_agg(u.\"Path\" ORDER BY u.\"Id\") FROM \"ScanUnscannedItems\" u WHERE u.\"ScanId\" = s.\"Id\"), '{}');");

            migrationBuilder.DropTable(
                name: "ScanUnscannedItems");
        }
    }
}
