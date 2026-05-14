using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <summary>
    /// Replaces legacy startup DDL for <c>user_offer_interactions</c>. If the table already exists from that script,
    /// drop it once (or baseline this row in <c>__EFMigrationsHistory</c>) before <c>Database.Migrate</c> runs.
    /// </summary>
    /// <inheritdoc />
    public partial class UserOfferInteractionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_offer_interactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_offer_interactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_offer_interactions_CreatedAt",
                table: "user_offer_interactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_user_offer_interactions_OfferId",
                table: "user_offer_interactions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_user_offer_interactions_OfferId_CreatedAt",
                table: "user_offer_interactions",
                columns: new[] { "OfferId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_offer_interactions_UserId",
                table: "user_offer_interactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_offer_interactions_UserId_CreatedAt",
                table: "user_offer_interactions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_offer_interactions");
        }
    }
}
