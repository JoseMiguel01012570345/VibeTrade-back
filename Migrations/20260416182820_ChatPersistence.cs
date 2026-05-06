using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_threads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuyerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InitiatorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstMessageSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_threads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SenderUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_notifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessagePreview = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AuthorStoreName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AuthorTrustScore = table.Column<int>(type: "integer", nullable: false),
                    SenderUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_notifications_chat_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "chat_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ThreadId",
                table: "chat_messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ThreadId_CreatedAtUtc",
                table: "chat_messages",
                columns: new[] { "ThreadId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_notifications_MessageId",
                table: "chat_notifications",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_notifications_RecipientUserId",
                table: "chat_notifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_notifications_RecipientUserId_CreatedAtUtc",
                table: "chat_notifications",
                columns: new[] { "RecipientUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_BuyerUserId",
                table: "chat_threads",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId",
                table: "chat_threads",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_OfferId_BuyerUserId",
                table: "chat_threads",
                columns: new[] { "OfferId", "BuyerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_threads_SellerUserId",
                table: "chat_threads",
                column: "SellerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_notifications");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_threads");
        }
    }
}
