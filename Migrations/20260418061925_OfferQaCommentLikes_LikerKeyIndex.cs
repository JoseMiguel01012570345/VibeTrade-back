using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class OfferQaCommentLikes_LikerKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_offer_qa_comment_likes_LikerKey",
                table: "offer_qa_comment_likes",
                column: "LikerKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_offer_qa_comment_likes_LikerKey",
                table: "offer_qa_comment_likes");
        }
    }
}
