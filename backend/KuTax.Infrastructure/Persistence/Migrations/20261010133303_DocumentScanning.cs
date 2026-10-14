using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScanStatus",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScannedAt",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Documents" SET "Status"='Stored' WHERE "ScanStatus"='Pending' AND "Status"<>'Quarantined';
                UPDATE "ProcessingJobs" SET "Status"='Queued', "Attempts"=0, "AvailableAt"=CURRENT_TIMESTAMP,
                    "LeaseUntil"=NULL, "LeaseToken"=NULL, "ErrorCode"=NULL
                WHERE "DocumentId" IN (SELECT "Id" FROM "Documents" WHERE "ScanStatus"='Pending' AND "Status"<>'Quarantined');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScanStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ScannedAt",
                table: "Documents");
        }
    }
}
