using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatThreadListHiddenToExpelled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SellerListHiddenAtUtc",
                table: "chat_threads",
                newName: "SellerExpelledAtUtc");

            migrationBuilder.RenameColumn(
                name: "BuyerListHiddenAtUtc",
                table: "chat_threads",
                newName: "BuyerExpelledAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SellerExpelledAtUtc",
                table: "chat_threads",
                newName: "SellerListHiddenAtUtc");

            migrationBuilder.RenameColumn(
                name: "BuyerExpelledAtUtc",
                table: "chat_threads",
                newName: "BuyerListHiddenAtUtc");
        }
    }
}
