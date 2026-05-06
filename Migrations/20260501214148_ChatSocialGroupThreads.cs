using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatSocialGroupThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSocialGroup",
                table: "chat_threads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "chat_social_group_members",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_social_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_social_group_members_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_social_group_members_ThreadId",
                table: "chat_social_group_members",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_social_group_members_ThreadId_UserId",
                table: "chat_social_group_members",
                columns: new[] { "ThreadId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_social_group_members");

            migrationBuilder.DropColumn(
                name: "IsSocialGroup",
                table: "chat_threads");
        }
    }
}
