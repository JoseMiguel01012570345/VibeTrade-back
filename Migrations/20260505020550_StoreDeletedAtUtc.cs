using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class StoreDeletedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stores_NormalizedName",
                table: "stores");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stores_NormalizedName",
                table: "stores",
                column: "NormalizedName",
                unique: true,
                filter: "\"NormalizedName\" IS NOT NULL AND \"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stores_NormalizedName",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "stores");

            migrationBuilder.CreateIndex(
                name: "IX_stores_NormalizedName",
                table: "stores",
                column: "NormalizedName",
                unique: true,
                filter: "\"NormalizedName\" IS NOT NULL");
        }
    }
}
