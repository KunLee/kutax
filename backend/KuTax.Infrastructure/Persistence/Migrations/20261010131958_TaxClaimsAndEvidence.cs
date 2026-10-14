using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxClaimsAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Allocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Allocations_Id_OwnerId",
                table: "Allocations",
                columns: new[] { "Id", "OwnerId" });

            migrationBuilder.CreateTable(
                name: "EvidenceLinks",
                columns: table => new
                {
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceLinks", x => new { x.AllocationId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_EvidenceLinks_Allocations_AllocationId_OwnerId",
                        columns: x => new { x.AllocationId, x.OwnerId },
                        principalTable: "Allocations",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceLinks_Documents_DocumentId_OwnerId",
                        columns: x => new { x.DocumentId, x.OwnerId },
                        principalTable: "Documents",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRules",
                columns: table => new
                {
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    PaperReference = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    MappingUrl = table.Column<string>(type: "text", nullable: false),
                    SourceSha256 = table.Column<string>(type: "text", nullable: false),
                    Conditions = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRules", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "TaxClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    RuleVersion = table.Column<string>(type: "character varying(100)", nullable: false),
                    AllocationVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    DeductibleAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxClaims_Allocations_AllocationId_OwnerId",
                        columns: x => new { x.AllocationId, x.OwnerId },
                        principalTable: "Allocations",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxClaims_TaxRules_RuleVersion",
                        column: x => x.RuleVersion,
                        principalTable: "TaxRules",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleReviews",
                columns: table => new
                {
                    RuleVersion = table.Column<string>(type: "character varying(100)", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleReviews", x => x.RuleVersion);
                    table.ForeignKey(
                        name: "FK_TaxRuleReviews_TaxRules_RuleVersion",
                        column: x => x.RuleVersion,
                        principalTable: "TaxRules",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "TaxRules",
                columns: new[] { "Version", "Category", "Conditions", "FinancialYearStart", "MappingUrl", "PaperReference", "Section", "SourceSha256", "SourceUrl" },
                values: new object[,]
                {
                    { "AU-2024-ProfessionalMembership-v1", "ProfessionalMembership", "Ordinary periodic fee for current employment only. Paid and incurred within this financial year, with service entirely within the year; written evidence retained. Excludes joining fees, levies, capital costs, unrelated memberships and prepayments for another year. Reconcile payroll and myTax prefill to avoid double claiming. This is deliberately limited coverage.", 2024, "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", "D5 E", "Other work-related expenses", "C4B8FA8BB372485CC8E66FF38CC62D1CF6FA2B12834380FDC88EEC3F6EE946CB", "https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001" },
                    { "AU-2024-UnionFees-v1", "UnionFees", "Ordinary periodic fee for current employment only. Paid and incurred within this financial year, with service entirely within the year; written evidence retained. Excludes joining fees, levies, capital costs, unrelated memberships and prepayments for another year. Reconcile payroll and myTax prefill to avoid double claiming. This is deliberately limited coverage.", 2024, "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", "D5 E", "Other work-related expenses", "C4B8FA8BB372485CC8E66FF38CC62D1CF6FA2B12834380FDC88EEC3F6EE946CB", "https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001" },
                    { "AU-2025-ProfessionalMembership-v1", "ProfessionalMembership", "Ordinary periodic fee for current employment only. Paid and incurred within this financial year, with service entirely within the year; written evidence retained. Excludes joining fees, levies, capital costs, unrelated memberships and prepayments for another year. Reconcile payroll and myTax prefill to avoid double claiming. This is deliberately limited coverage.", 2025, "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", "D5 E", "Other work-related expenses", "5EFF5D035035E7B0898B8B316A6CC1D6B616577E10157A9D97B1B5CD30A68BF5", "https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001" },
                    { "AU-2025-UnionFees-v1", "UnionFees", "Ordinary periodic fee for current employment only. Paid and incurred within this financial year, with service entirely within the year; written evidence retained. Excludes joining fees, levies, capital costs, unrelated memberships and prepayments for another year. Reconcile payroll and myTax prefill to avoid double claiming. This is deliberately limited coverage.", 2025, "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", "D5 E", "Other work-related expenses", "5EFF5D035035E7B0898B8B316A6CC1D6B616577E10157A9D97B1B5CD30A68BF5", "https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceLinks_AllocationId_OwnerId",
                table: "EvidenceLinks",
                columns: new[] { "AllocationId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceLinks_DocumentId_OwnerId",
                table: "EvidenceLinks",
                columns: new[] { "DocumentId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxClaims_AllocationId_AllocationVersion_TransactionVersion",
                table: "TaxClaims",
                columns: new[] { "AllocationId", "AllocationVersion", "TransactionVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxClaims_AllocationId_OwnerId",
                table: "TaxClaims",
                columns: new[] { "AllocationId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxClaims_RuleVersion",
                table: "TaxClaims",
                column: "RuleVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvidenceLinks");

            migrationBuilder.DropTable(
                name: "TaxClaims");

            migrationBuilder.DropTable(
                name: "TaxRuleReviews");

            migrationBuilder.DropTable(
                name: "TaxRules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Allocations_Id_OwnerId",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Allocations");
        }
    }
}
