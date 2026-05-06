using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessageGroupReceiptsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupReceiptsJson",
                table: "chat_messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupReceiptsJson",
                table: "chat_messages");
        }
    }
}
