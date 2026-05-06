using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "chat_threads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads",
                columns: new[] { "OfferId", "BuyerUserId" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "chat_messages");

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads",
                columns: new[] { "OfferId", "BuyerUserId" },
                unique: true);
        }
    }
}
