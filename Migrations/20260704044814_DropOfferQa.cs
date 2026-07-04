using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class DropOfferQa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_qa_comment_likes");

            migrationBuilder.DropColumn(
                name: "OfferQaJson",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "OfferQaJson",
                table: "store_products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfferQaJson",
                table: "store_services",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OfferQaJson",
                table: "store_products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "offer_qa_comment_likes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LikerKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QaCommentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_qa_comment_likes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_qa_comment_likes_LikerKey",
                table: "offer_qa_comment_likes",
                column: "LikerKey");

            migrationBuilder.CreateIndex(
                name: "IX_offer_qa_comment_likes_OfferId_QaCommentId",
                table: "offer_qa_comment_likes",
                columns: new[] { "OfferId", "QaCommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_offer_qa_comment_likes_OfferId_QaCommentId_LikerKey",
                table: "offer_qa_comment_likes",
                columns: new[] { "OfferId", "QaCommentId", "LikerKey" },
                unique: true);
        }
    }
}
