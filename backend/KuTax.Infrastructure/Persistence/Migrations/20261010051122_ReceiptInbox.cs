using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedFiles = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.UniqueConstraint("AK_ImportBatches_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_ImportBatches_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.UniqueConstraint("AK_Documents_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_Documents_ImportBatches_ImportBatchId_OwnerId",
                        columns: x => new { x.ImportBatchId, x.OwnerId },
                        principalTable: "ImportBatches",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportItems",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportItems", x => new { x.BatchId, x.ClientId });
                    table.ForeignKey(
                        name: "FK_ImportItems_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportItems_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ImportBatchId_OwnerId",
                table: "Documents",
                columns: new[] { "ImportBatchId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerId_Sha256",
                table: "Documents",
                columns: new[] { "OwnerId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_OwnerId",
                table: "ImportBatches",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportItems_DocumentId",
                table: "ImportItems",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportItems");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
