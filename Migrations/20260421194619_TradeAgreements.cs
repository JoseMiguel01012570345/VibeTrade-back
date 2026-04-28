using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class TradeAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_agreements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedByStoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuerLabel = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RespondedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SellerEditBlockedUntilBuyerResponse = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeMerchandise = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeService = table.Column<bool>(type: "boolean", nullable: false),
                    MerchandiseJson = table.Column<string>(type: "jsonb", nullable: false),
                    MerchandiseMetaJson = table.Column<string>(type: "jsonb", nullable: true),
                    ServicesJson = table.Column<string>(type: "jsonb", nullable: false),
                    LegacyServiceBlockJson = table.Column<string>(type: "jsonb", nullable: true),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RouteSheetUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreements_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreements_ThreadId",
                table: "trade_agreements",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreements_ThreadId_Status",
                table: "trade_agreements",
                columns: new[] { "ThreadId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_agreements");
        }
    }
}
