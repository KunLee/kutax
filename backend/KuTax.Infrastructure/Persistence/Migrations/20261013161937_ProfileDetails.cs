using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Avatars",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Avatars", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Avatars_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Avatars");

            migrationBuilder.DropColumn(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Occupation",
                table: "AspNetUsers");
        }
    }
}
