using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobby.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuestPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PairToken",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PairTokenExpiresAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PairToken",
                table: "AspNetUsers",
                column: "PairToken",
                unique: true,
                filter: "[PairToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PairToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PairToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PairTokenExpiresAt",
                table: "AspNetUsers");
        }
    }
}
