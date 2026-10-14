using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<string>(type: "text", nullable: false),
                    CandidateJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractionAttempts_Documents_DocumentId_OwnerId",
                        columns: x => new { x.DocumentId, x.OwnerId },
                        principalTable: "Documents",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivacyPreferences",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    ExternalAiConsent = table.Column<bool>(type: "boolean", nullable: false),
                    NoticeVersion = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivacyPreferences", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_PrivacyPreferences_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Documents_DocumentId_OwnerId",
                        columns: x => new { x.DocumentId, x.OwnerId },
                        principalTable: "Documents",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Merchant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Gst = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.UniqueConstraint("AK_Transactions_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_Transactions_Documents_DocumentId_OwnerId",
                        columns: x => new { x.DocumentId, x.OwnerId },
                        principalTable: "Documents",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OwnerId_ResourceId",
                table: "AuditEvents",
                columns: new[] { "OwnerId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_DocumentId_OwnerId",
                table: "ExtractionAttempts",
                columns: new[] { "DocumentId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId",
                table: "ProcessingJobs",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_OwnerId",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_Status_AvailableAt",
                table: "ProcessingJobs",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DocumentId",
                table: "Transactions",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DocumentId_OwnerId",
                table: "Transactions",
                columns: new[] { "DocumentId", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ExtractionAttempts");

            migrationBuilder.DropTable(
                name: "PrivacyPreferences");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
