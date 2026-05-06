using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class TradeAgreementSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "trade_agreements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "trade_agreements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreements_DeletedAtUtc",
                table: "trade_agreements",
                column: "DeletedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trade_agreements_DeletedAtUtc",
                table: "trade_agreements");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "trade_agreements");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "trade_agreements");
        }
    }
}
