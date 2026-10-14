using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnnualExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The preceding additive migration used CLR false for existing rows. Restore only
            // the latest saved set recorded in the existing audit trail, never superseded sets.
            migrationBuilder.Sql("""
                WITH latest AS (
                    SELECT DISTINCT ON ("ResourceId") "DetailsJson" FROM "AuditEvents"
                    WHERE "Action"='AllocationsReplaced' ORDER BY "ResourceId", "CreatedAt" DESC
                ), current_ids AS (
                    SELECT (item->>'id')::uuid AS id FROM latest,
                    jsonb_array_elements("DetailsJson"->'after') item
                ) UPDATE "Allocations" SET "IsActive"=true WHERE "Id" IN (SELECT id FROM current_ids);
                """);
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Allocations",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.CreateTable(
                name: "ExportSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    ApprovedBy = table.Column<string>(type: "text", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_ExportSnapshots_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_ExportSnapshots_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntryProgress",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryProgress", x => new { x.SnapshotId, x.Section });
                    table.ForeignKey(
                        name: "FK_EntryProgress_ExportSnapshots_SnapshotId_OwnerId",
                        columns: x => new { x.SnapshotId, x.OwnerId },
                        principalTable: "ExportSnapshots",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    FilesJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportJobs_ExportSnapshots_Id_OwnerId",
                        columns: x => new { x.Id, x.OwnerId },
                        principalTable: "ExportSnapshots",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntryProgress_SnapshotId_OwnerId",
                table: "EntryProgress",
                columns: new[] { "SnapshotId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_Id_OwnerId",
                table: "ExportJobs",
                columns: new[] { "Id", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_Status_AvailableAt",
                table: "ExportJobs",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportSnapshots_OwnerId_FinancialYearStart",
                table: "ExportSnapshots",
                columns: new[] { "OwnerId", "FinancialYearStart" });
            migrationBuilder.Sql("""
                CREATE FUNCTION kutax_reject_snapshot_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Approved snapshots and claim decisions are immutable'; END $$;
                CREATE TRIGGER kutax_snapshot_immutable BEFORE UPDATE ON "ExportSnapshots" FOR EACH ROW EXECUTE FUNCTION kutax_reject_snapshot_change();
                CREATE TRIGGER kutax_claim_immutable BEFORE UPDATE ON "TaxClaims" FOR EACH ROW EXECUTE FUNCTION kutax_reject_snapshot_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS kutax_claim_immutable ON \"TaxClaims\"; DROP FUNCTION kutax_reject_snapshot_change() CASCADE;");
            migrationBuilder.DropTable(
                name: "EntryProgress");

            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropTable(
                name: "ExportSnapshots");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Allocations",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
