using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatThreadsMultipleActivePerOfferBuyer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads");

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads",
                columns: new[] { "OfferId", "BuyerUserId" },
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads");

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads",
                columns: new[] { "OfferId", "BuyerUserId" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");
        }
    }
}
