using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "ImportBatches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Receipt");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Receipt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Documents");
        }
    }
}
