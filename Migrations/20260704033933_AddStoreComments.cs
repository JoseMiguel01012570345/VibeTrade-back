using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommentsJson",
                table: "stores",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "store_qa_comment_likes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LikerKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_qa_comment_likes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_store_qa_comment_likes_LikerKey",
                table: "store_qa_comment_likes",
                column: "LikerKey");

            migrationBuilder.CreateIndex(
                name: "IX_store_qa_comment_likes_StoreId_CommentId",
                table: "store_qa_comment_likes",
                columns: new[] { "StoreId", "CommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_store_qa_comment_likes_StoreId_CommentId_LikerKey",
                table: "store_qa_comment_likes",
                columns: new[] { "StoreId", "CommentId", "LikerKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_qa_comment_likes");

            migrationBuilder.DropColumn(
                name: "CommentsJson",
                table: "stores");
        }
    }
}
