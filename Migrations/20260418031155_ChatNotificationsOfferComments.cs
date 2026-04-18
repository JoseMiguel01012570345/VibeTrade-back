using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatNotificationsOfferComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chat_notifications_chat_messages_MessageId",
                table: "chat_notifications");

            migrationBuilder.DropIndex(
                name: "IX_chat_notifications_MessageId",
                table: "chat_notifications");

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "chat_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "MessageId",
                table: "chat_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "OfferId",
                table: "chat_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_notifications_OfferId",
                table: "chat_notifications",
                column: "OfferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_notifications_OfferId",
                table: "chat_notifications");

            migrationBuilder.DropColumn(
                name: "OfferId",
                table: "chat_notifications");

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "chat_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessageId",
                table: "chat_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_notifications_MessageId",
                table: "chat_notifications",
                column: "MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_chat_notifications_chat_messages_MessageId",
                table: "chat_notifications",
                column: "MessageId",
                principalTable: "chat_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
