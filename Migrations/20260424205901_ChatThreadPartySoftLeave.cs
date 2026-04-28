using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatThreadPartySoftLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BuyerListHiddenAtUtc",
                table: "chat_threads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PartyExitedAtUtc",
                table: "chat_threads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyExitedReason",
                table: "chat_threads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyExitedUserId",
                table: "chat_threads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SellerListHiddenAtUtc",
                table: "chat_threads",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerListHiddenAtUtc",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "PartyExitedAtUtc",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "PartyExitedReason",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "PartyExitedUserId",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "SellerListHiddenAtUtc",
                table: "chat_threads");
        }
    }
}
