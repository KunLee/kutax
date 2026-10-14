using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForeignCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRate",
                table: "Transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ConversionRateDate",
                table: "Transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionSource",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "Transactions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCurrency",
                table: "Transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitsPerAud = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => new { x.Currency, x.Date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropColumn(
                name: "ConversionRate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ConversionRateDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ConversionSource",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OriginalCurrency",
                table: "Transactions");
        }
    }
}
