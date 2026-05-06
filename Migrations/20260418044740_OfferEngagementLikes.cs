using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class OfferEngagementLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offer_likes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LikerKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_likes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "offer_qa_comment_likes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QaCommentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LikerKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_qa_comment_likes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_likes_LikerKey",
                table: "offer_likes",
                column: "LikerKey");

            migrationBuilder.CreateIndex(
                name: "IX_offer_likes_OfferId",
                table: "offer_likes",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_offer_likes_OfferId_LikerKey",
                table: "offer_likes",
                columns: new[] { "OfferId", "LikerKey" },
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_likes");

            migrationBuilder.DropTable(
                name: "offer_qa_comment_likes");
        }
    }
}
