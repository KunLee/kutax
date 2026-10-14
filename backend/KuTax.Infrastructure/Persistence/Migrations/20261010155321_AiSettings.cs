using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TextModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RetentionNotice = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ApiKeyHint = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSettings", x => x.Id);
                    table.CheckConstraint("CK_AiSettings_Singleton", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSettings");
        }
    }
}
