using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatRouteSheetsEmergentOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_route_sheets",
                columns: table => new
                {
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedToPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_route_sheets", x => new { x.ThreadId, x.RouteSheetId });
                    table.ForeignKey(
                        name: "FK_chat_route_sheets_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "emergent_offers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublisherUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetractedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergent_offers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_route_sheets_ThreadId",
                table: "chat_route_sheets",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_emergent_offers_Kind_RetractedAtUtc_PublishedAtUtc",
                table: "emergent_offers",
                columns: new[] { "Kind", "RetractedAtUtc", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_emergent_offers_OfferId",
                table: "emergent_offers",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_emergent_offers_ThreadId_RouteSheetId",
                table: "emergent_offers",
                columns: new[] { "ThreadId", "RouteSheetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_route_sheets");

            migrationBuilder.DropTable(
                name: "emergent_offers");
        }
    }
}
